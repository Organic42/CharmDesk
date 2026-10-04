<div align="center">

![CharmDesk](docs/banner.png)

[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-6D28D9?style=flat-square)](#)
[![.NET](https://img.shields.io/badge/.NET-8.0-6D28D9?style=flat-square)](#)
[![Code: MIT](https://img.shields.io/badge/code-MIT-6D28D9?style=flat-square)](LICENSE) [![Art: all rights reserved](https://img.shields.io/badge/art-all%20rights%20reserved-6D28D9?style=flat-square)](LICENSE-ART.md)
[![Latest release](https://img.shields.io/github/v/release/Organic42/CharmDesk?style=flat-square&color=FF5FA2&label=release)](https://github.com/Organic42/CharmDesk/releases/latest)

**Little pixel-art charms that hang off the edge of your desktop and swing like the real thing.**

</div>

---

CharmDesk pins a collectible charm — a gilded evil eye, a ghost, whatever you add next — to the top
of your screen. Grab it with your mouse, pull it, let go, and it swings and settles like an
actual object on a string, not a CSS animation pretending to be one. It sits quietly in the
system tray the rest of the time.

## Why

Phone charms and bag charms are everywhere right now. Desktops don't get to have any. CharmDesk
is a small, honest attempt to fix that — a hanging ornament for the one screen you look at all
day, built as a real physics simulation instead of a sprite that jiggles on a timer.

## Features

- **Physical, not animated.** A polar pendulum-spring simulation drives every swing — grab
  momentum, gravity, damping, and secondary string motion all fall out of the same simulation,
  free-swinging and cursor-grabbed alike.
- **Actually transparent.** The overlay is click-through everywhere except the charm itself, so
  it never steals a click meant for your desktop or the app underneath it.
- **A real charm library.** Every charm is its own package — a folder with a `manifest.json` and
  a transparent PNG. Nothing about a charm is hardcoded into the engine.
- **A local Charm Manager.** Import a PNG, tune its physics on live sliders, preview the drag
  feel, save. No source changes, no rebuild.
- **Lives in the tray.** Show/hide, switch charms, reposition, or quit — right-click the charm
  itself or the tray icon for the same menu.
- **Shut down or sleep later.** Schedule your PC to shut down or sleep in 30 minutes, an hour,
  two hours, or at a set time. A one-minute countdown you can cancel always comes first, and
  apps with unsaved work still get to ask before they close.

## The collection so far

<table>
<tr>
<td align="center" width="33%"><img src="charms/timekeeper/thumbnail.png" width="120"><br><b>Timekeeper</b><br><sub>Flagship - live clock</sub></td>
<td align="center" width="33%"><img src="charms/evil-eye-ornate/thumbnail.png" width="120"><br><b>Gilded Evil Eye</b><br><sub>Traditional</sub></td>
<td align="center" width="33%"><img src="charms/cute-ghost/thumbnail.png" width="120"><br><b>Boo</b><br><sub>Cute</sub></td>
</tr>
</table>

**Timekeeper** is the one charm with a reason to stay on your desktop beyond looks: a live
clock rendered on top of the charm art in real time (not baked into the image), so it swings,
spins, and tells the actual time. See [Live-rendered charms](#live-rendered-charms) below for
how that works.

## Building a charm package

A charm is just a folder — drop one into your charm library folder and CharmDesk picks it up on
next launch, no code involved:

```text
charms/
  cute-ghost/
    manifest.json
    charm.png        # transparent PNG, pixel art recommended
    thumbnail.png
```

```json
{
  "id": "cute-ghost",
  "name": "Boo",
  "description": "A friendly little ghost with a heart to give away.",
  "category": "Cute",
  "image": "charm.png",
  "thumbnail": "thumbnail.png",
  "enabled": true,
  "displayScale": 1.0,
  "reactionStyle": "Playful",
  "physics": {
    "stringLength": 118,
    "gravity": 980,
    "damping": 0.94,
    "stiffness": 0.15,
    "mass": 0.85
  }
}
```

Every physics value is per-charm — a heavier charm drags differently than a light one. The
in-app Charm Manager writes exactly this file for you, if you'd rather not hand-edit JSON.

## Live-rendered charms

Every charm above is a static image. Timekeeper isn't — its art is a blank display face, and
`manifest.json` declares a `clockFace.digital` region (position, size, colors) in the source
image's own pixel coordinates. At runtime `CharmWindow` draws live text into that region on a
dedicated 1Hz timer, independent of the 60fps physics loop, so the clock keeps ticking even
while the charm is sitting still (and the physics loop is allowed to sleep). The text layer
lives inside the same transform as the charm image, so it inherits the pendulum's position and
spin automatically — it swings and does a 360° with the rest of the charm on a double-click,
exactly like a normal charm, with zero interaction code written specifically for it.

```json
"clockFace": {
  "digital": {
    "x": 296, "y": 305, "width": 424, "height": 351,
    "showDate": true,
    "timeColor": "#5FD4FF", "dateColor": "#3E8FB0"
  }
}
```

Any charm can opt into this the same way; nothing else changes. It's a small, deliberately
narrow mechanism (one region type, digital text) rather than a general plugin system — easy to
extend later (an analog hands layer, say) if a second live charm actually needs it.

## Getting started

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) and Windows 10/11.

```bash
git clone https://github.com/Organic42/CharmDesk.git
cd CharmDesk/src
dotnet build CharmDesk.sln -c Release
dotnet run --project CharmDesk
```

To publish a standalone build:

```bash
dotnet publish CharmDesk/CharmDesk.csproj -c Release -o ./out
```

This produces a framework-dependent build (a few MB, requires the .NET 8 Desktop Runtime on the
target machine) — the right shape for Microsoft Store distribution, where the runtime ships as
an automatic dependency.

## How it's built

Native WPF + a thin sliver of WinForms (tray icon, monitor enumeration only) — no Electron, no
web view. Chosen specifically for real transparent, click-through windows and a system tray
that doesn't need a browser runtime behind it.

```text
Commerce/        optional tip jar - Store in-app purchases when packaged, a donation
                 link fallback when not (see packaging/tip-jar-setup.md)
Core/            physics engine, charm manifest model, interaction system
Native/          Win32 interop - the window region behind click-through, DWM
                 transparency, DPI/monitor helpers
Persistence/     settings + logging, both plain JSON/text files
Power/           shut down / sleep later - timing rules, the scheduler, and the
                 only code that asks Windows to shut down or sleep
Tray/            the system tray icon and its shared context menu
Windows/         the desktop overlay, Charm Library, Charm Manager, Settings
```

The trickiest bit is click-through: the overlay is a large window, and it must ignore every
click except the ones on the charm. CharmDesk does that with a **window region**
(`SetWindowRgn`): the window is reshaped to just the charm, its anchor and a thin band along the
string, and rebuilt as the charm swings. Everything outside that shape simply isn't part of the
window, so Windows hands those clicks straight to whatever is underneath.

Two more obvious approaches were tried first and both failed:

- **`WS_EX_TRANSPARENT`** removes the window from hit-testing *entirely*, so the charm can't be
  clicked either. Switching it off again needed a global mouse hook, and that proved unreliable:
  on some laptops the charm ignored the mouse completely, and after a single hover the overlay
  could go on swallowing every click around it.
- **Returning `HTTRANSPARENT` from `WM_NCHITTEST`** only passes clicks to windows owned by the
  *same thread*, so clicks never reach other apps.

The overlay is drawn through the desktop compositor (DWM) rather than as a WPF layered window,
which is what fixed the flicker some laptops showed. The old layered path is still available as
a fallback: set `CHARMDESK_RENDER=layered`.

## Roadmap

- [x] Core physics + drag interaction
- [x] Charm Library, Manager, Settings
- [x] Framework-dependent, trimmed-down publish
- [x] MSIX packaging (see [packaging/](packaging/)) - builds and installs locally; Store submission needs a real Partner Center identity swapped in
- [ ] Store listing (icons, screenshots, privacy policy)

## Contributing

Issues and PRs welcome. If you build a charm you like, a PR adding it to `charms/` is the easiest
way to get it into the collection. By opening one, you confirm you made the art yourself and give
permission for it to be distributed as part of CharmDesk, in free or paid versions. You keep the
copyright to your charm.

## License

CharmDesk is split into two parts:

| Part | Licence |
|---|---|
| **Source code** | [MIT](LICENSE) — use, modify and redistribute it freely. |
| **Artwork**: the charms, icons, store images and banner | **All rights reserved** — see [LICENSE-ART.md](LICENSE-ART.md). Use it with CharmDesk and share screenshots or videos, but don't redistribute or reuse it. |
| **Fonts**: Fredoka, Space Mono, Silkscreen | [SIL Open Font License 1.1](https://openfontlicense.org), by their respective authors. |
