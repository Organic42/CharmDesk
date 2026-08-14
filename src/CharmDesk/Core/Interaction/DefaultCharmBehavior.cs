using System;
using CharmDesk.Audio;
using CharmDesk.Core.Models;

namespace CharmDesk.Core.Interaction;

/// <summary>
/// The stock reaction set every charm gets, parameterized by its manifest's
/// <see cref="ReactionStyle"/> - a "personality" knob rather than a bespoke behavior class per
/// charm, so any charm package can opt into a punchier or gentler feel just by data.
///
/// Sound is separately gated by <paramref name="soundEnabled"/> (checked at play time, not
/// captured once) so it stays in sync with the live Settings toggle. Passing null keeps a
/// behavior silent - used for the Manager/Library preview instances, which shouldn't chime on
/// every slider drag.
/// </summary>
public sealed class DefaultCharmBehavior : ICharmBehavior
{
    private readonly ReactionStyle _style;
    private readonly Func<bool>? _soundEnabled;

    public DefaultCharmBehavior(ReactionStyle style = ReactionStyle.Default, Func<bool>? soundEnabled = null)
    {
        _style = style;
        _soundEnabled = soundEnabled;
    }

    private double Intensity => _style switch
    {
        ReactionStyle.Playful => 1.4,
        ReactionStyle.Gentle => 0.6,
        ReactionStyle.Dramatic => 1.8,
        _ => 1.0,
    };

    private bool SoundOn => _soundEnabled?.Invoke() == true;

    public void OnHoverEnter(PhysicsEngine engine) =>
        engine.Nudge(0.045 * (_style == ReactionStyle.Playful ? 1.6 : 1.0));

    public void OnHoverExit(PhysicsEngine engine) { }

    public void OnGrab(PhysicsEngine engine)
    {
        if (SoundOn) SoundEffects.PlayPickup();
    }

    public void OnRelease(PhysicsEngine engine) { }

    public void OnClick(PhysicsEngine engine)
    {
        engine.Bounce(Intensity);
        if (SoundOn) SoundEffects.PlayBounce();
    }

    public void OnDoubleClick(PhysicsEngine engine)
    {
        engine.Spike(Intensity);
        if (_style == ReactionStyle.Dramatic) engine.Nudge(0.3); // extra swing kick, for flair
        if (SoundOn) SoundEffects.PlaySpin();
    }
}
