"""Synthesizes CharmDesk's three UI chimes from scratch (sine partials + exponential decay,
bell-like additive synthesis) - no external audio assets, no licensing to worry about.
"""
import math
import os
import struct
import wave

SR = 44100
OUT_DIR = os.path.join(os.path.dirname(__file__), "..", "src", "CharmDesk", "Sounds")


def note(freq, duration, amp=1.0, partials=((1.0, 1.0), (2.01, 0.35), (3.0, 0.12)), decay=5.5):
    """One bell-like tone: a few sine partials, each with its own exponential decay."""
    n = int(SR * duration)
    samples = [0.0] * n
    for mult, weight in partials:
        w = 2 * math.pi * freq * mult
        for i in range(n):
            t = i / SR
            samples[i] += math.sin(w * t) * weight * math.exp(-decay * t)
    peak = max(abs(s) for s in samples) or 1.0
    return [s / peak * amp for s in samples]


def mix(*tracks, gap=0.0):
    """Concatenates tracks with a small silent gap between them."""
    gap_samples = [0.0] * int(SR * gap)
    out = []
    for i, t in enumerate(tracks):
        out.extend(t)
        if i < len(tracks) - 1:
            out.extend(gap_samples)
    return out


def overlay(*tracks):
    """Sums tracks sample-by-sample (for simultaneous notes / chords)."""
    n = max(len(t) for t in tracks)
    out = [0.0] * n
    for t in tracks:
        for i, v in enumerate(t):
            out[i] += v
    peak = max(abs(s) for s in out) or 1.0
    if peak > 1.0:
        out = [s / peak for s in out]
    return out


def save(path, samples):
    with wave.open(path, "w") as f:
        f.setnchannels(1)
        f.setsampwidth(2)
        f.setframerate(SR)
        frames = b"".join(struct.pack("<h", max(-32767, min(32767, int(s * 28000)))) for s in samples)
        f.writeframes(frames)
    print(f"wrote {path} ({len(samples) / SR:.2f}s)")


if __name__ == "__main__":
    os.makedirs(OUT_DIR, exist_ok=True)

    # Pickup: a quick two-note rising chime (like lifting something light off a hook).
    pickup = mix(note(659.25, 0.14, amp=0.8, decay=9), note(987.77, 0.22, amp=0.9, decay=6), gap=0.02)
    save(os.path.join(OUT_DIR, "pickup.wav"), pickup)

    # Bounce: one soft, short pop-chime for the click reaction.
    bounce = note(523.25, 0.16, amp=0.9, decay=10)
    save(os.path.join(OUT_DIR, "bounce.wav"), bounce)

    # Spin: a brighter three-note ascending flourish for the double-click spin.
    spin = mix(
        note(587.33, 0.10, amp=0.7, decay=12),
        note(739.99, 0.10, amp=0.8, decay=12),
        note(1174.66, 0.28, amp=0.9, decay=7),
        gap=0.015,
    )
    save(os.path.join(OUT_DIR, "spin.wav"), spin)
