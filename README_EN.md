# OPPO Pods For Windows

[中文](README.md) | [English](README_EN.md)

---

Manage your OPPO / OnePlus / realme Bluetooth earbuds right from your Windows desktop — check battery, switch noise cancelling, tune EQ, and manage multi-device connections without opening the phone app.

Supports **137 devices** across all three brands, with capabilities auto-detected per model — the UI only shows what your earbuds actually support.

---

## Table of Contents

- [Features](#features)
- [Requirements](#requirements)
- [Getting Started](#getting-started)
- [Interface Guide](#interface-guide)
- [Supported Devices](#supported-devices)
- [FAQ](#faq)
- [Maintainers](#maintainers)
- [Acknowledgements](#acknowledgements)
- [License](#license)

---

## Features

### Battery & Wear
- **Three readings**: Left / Right / Case shown separately, with charging status ⚡
- **Wear detection**: in-case / worn / removed, in real time
- **Connection card**: a semi-transparent battery card slides in at the bottom-right on connect
- **Battery alerts**: automatic pop-ups for low (≤20%) and critical (≤10%) battery

### Noise Cancelling
- **Dynamic ANC modes**: main modes shown per model (Off / NC / Transparency / Adaptive)
- **ANC sub-levels**: Smart / Deep / Medium / Light, listed automatically per model
- **Smart real-time level**: in Smart mode, shows the level the device computes on the fly (e.g. "Real-time: Deep"), matching the official app

### Sound
- **Master EQ**: available presets loaded per model, one-tap switching
- **Spatial sound**: on/off spatial soundstage
- **Spatial audio 3-mode**: Off / Fixed / Head Tracking (supported models)

### Other Controls
- **Game mode**: low-latency toggle, standard and compatible implementations
- **Dual-device connection**: toggle simultaneous two-device connection
- **Multi-device management**: view connected devices, switch the active one with one click
- **Device info**: firmware version and audio codec display

### Desktop Experience
- **System tray**: left-click toggles the window, right-click opens a menu, hover shows live battery
- **Minimize to tray**: closing the window minimizes to tray instead of quitting (optional)
- **Auto-start**: launch with Windows (optional)
- **Auto-reconnect**: reconnects automatically after a disconnection
- **Theme**: follows system light/dark, or set it manually

---

## Requirements

- **OS**: Windows 10 / 11 (64-bit)
- **Hardware**: a Bluetooth adapter + paired OPPO / OnePlus / realme earbuds
- **Dependencies**: the self-contained release needs nothing installed — just run it

---

## Getting Started

1. Download the latest build from [Releases](https://github.com/Zhaoyi-ya/OPPO-Pods-For-Windows/releases)
2. Unzip anywhere
3. **Pair your earbuds first** in Windows Bluetooth settings (this matters — the app doesn't handle pairing)
4. Run it — the app discovers and connects to paired earbuds automatically

Once connected, the main window shows battery and all controls. Your settings (theme, model override, tray/auto-start preferences) are remembered.

---

## Interface Guide

Main window, top to bottom:

| Section | Description |
|---------|-------------|
| **Device list** | Shown for dual-device models; expand to view connected devices and switch the active one |
| **Battery** | Left / Case / Right readings with charging status; wear status below |
| **Noise Cancelling** | Main-mode segmented selector + sub-levels (generated per model); shows real-time level in Smart mode |
| **Spatial Audio** | 3-mode selection (only shown on supported models) |
| **Features** | Spatial sound, game mode, dual-device toggles + Master EQ dropdown |
| **Status bar** | Connection indicator + reconnect button |

**Settings page**: manual model override (searchable), custom device name, game-mode implementation, theme, minimize-to-tray, auto-start.

**About page**: version info and acknowledgements.

---

## Supported Devices

137 models across three brands, synced from the official app:

| Brand | Series |
|-------|--------|
| OPPO | Enco Free / Air / X / R / Clip / Buds |
| OnePlus | Buds Pro / Nord Buds / Bullets Wireless |
| realme | Buds Air / Buds T / Buds Wireless / DIZO |

Features vary by model (ANC sub-levels, spatial audio, dual-device, Master EQ, etc.). The app **shows only what your model supports** — unsupported features never appear. If auto-detection is wrong, search and pick your model under **Settings → Device Model**.

---

## FAQ

**Q: The app can't find my earbuds?**
First make sure they're paired and connected in Windows Settings → Bluetooth & devices. The app only connects to already-paired devices; it doesn't handle pairing.

**Q: Connected, but some features are missing?**
The UI adapts to your model — features your earbuds don't support (no spatial audio, no ANC sub-levels, etc.) won't appear. If the model is misidentified, pick the right one in Settings.

**Q: Wrong model / incomplete features?**
Use the search box under **Settings → Device Model** to find and set your exact model.

**Q: ANC level names differ from the official app?**
The ANC level names (Smart / Deep / Medium / Light) match the official app. Smart mode switches levels based on your environment, and the UI shows the currently computed level.

**Q: The app keeps running after I close the window?**
If "minimize to tray" is enabled, closing the window just hides it to the tray. Right-click the tray icon to quit.

---

## Maintainers

- [@Zhaoyi-ya](https://github.com/Zhaoyi-ya) — Repository author
- [@Dszsu](https://github.com/Dszsu) — Lead maintainer

---

## Acknowledgements

- [Leaf-lsgtky/OppoPods](https://github.com/Leaf-lsgtky/OppoPods) — OPPO proprietary protocol reverse engineering
- [1812z/OppoPods](https://github.com/1812z/OppoPods) — Feature implementation reference

---

## License

GPL-3.0
