<p align="right">
  <b>English</b> · <a href="README.id.md">Bahasa Indonesia</a>
</p>

<p align="center">
  <img src="docs/images/logo.png" width="112" alt="LumiDisplay logo" />
</p>

<h1 align="center">LumiDisplay</h1>

<p align="center">
  Control your monitor's <b>brightness</b>, <b>contrast</b> and <b>speaker volume</b> from the keyboard on Windows,<br>
  with a clean macOS-style on-screen display.
</p>

<p align="center">
  <a href="https://github.com/andikatahang/LumiDisplay/releases/latest"><img src="https://img.shields.io/github/v/release/andikatahang/LumiDisplay?color=1E3A8A&label=release" alt="Latest release"></a>
  <img src="https://img.shields.io/badge/Windows-10%20%7C%2011%20(x64)-1E3A8A" alt="Windows 10 | 11">
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-64748B" alt="MIT license"></a>
</p>

<p align="center">
  <a href="https://github.com/andikatahang/LumiDisplay/releases/latest/download/LumiDisplay.exe"><b>⬇ Download LumiDisplay.exe</b></a>
  &nbsp;·&nbsp;
  <a href="https://github.com/andikatahang/LumiDisplay/releases">All releases</a>
</p>

<p align="center">
  <img src="docs/images/hero.png" alt="LumiDisplay settings window, tray menu and brightness overlay" />
</p>

## Features

- **Hardware brightness & contrast** for external monitors over DDC/CI, and for laptop screens.
- **Monitor speaker volume** — your keyboard's volume and mute keys control the monitor's built-in speakers.
- **Keyboard shortcuts** for brightness (standard and fine steps) and contrast.
- **On-screen display** with 16 steps, like macOS, so you always see the current level.
- **Lives in the system tray** — quick presets, per-monitor targeting, and it can start with Windows.
- **Portable** — a single `.exe`, no installer, no .NET runtime to install.

## Install

1. **[Download `LumiDisplay.exe`](https://github.com/andikatahang/LumiDisplay/releases/latest/download/LumiDisplay.exe)** and move it to a permanent folder, for example `C:\Users\<you>\Apps\LumiDisplay\`.
2. **Double-click it.** If Windows SmartScreen shows *"Windows protected your PC"*, click **More info → Run anyway** (the app is not code-signed yet).
3. The settings window opens. Turn on **Start with Windows** if you want LumiDisplay running after every sign-in.

> **External monitor not responding?** Open the monitor's own on-screen menu and make sure **DDC/CI** is turned **On**. Most monitors ship with it on, some don't.

**Requirements:** Windows 10 (1809+) or Windows 11, 64-bit.

## Using LumiDisplay

| Shortcut | Action |
|---|---|
| <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>↑</kbd> / <kbd>↓</kbd> | Brightness up / down |
| <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>Shift</kbd> + <kbd>↑</kbd> / <kbd>↓</kbd> | Brightness up / down in fine steps |
| <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>→</kbd> / <kbd>←</kbd> | Contrast up / down |
| <kbd>Volume +</kbd> / <kbd>Volume −</kbd> / <kbd>Mute</kbd> | Monitor speaker volume and mute |

**Make the shortcuts your own:** open **Settings → Hotkeys**, click a shortcut and press the keys you want. You can change every brightness and contrast shortcut, and add your own keys for monitor volume up/down and mute. Backspace clears a shortcut, **Reset to defaults** brings back the ones above.

- **Open the window:** launch `LumiDisplay.exe` again, or click the tray icon.
- **Close the window:** LumiDisplay keeps running in the tray so your keys keep working.
- **Quit completely:** click **Quit LumiDisplay** in the window, or right-click the tray icon → **Quit LumiDisplay**.
- **Right-click the tray icon** for brightness/volume presets (0–100%), choosing which monitor to control, and monitor info.
- **Start over:** **Settings → General → Reset all** restores every option and shortcut to its default (applied when you click **Save changes**).

## Screenshots

| | |
|---|---|
| ![General settings](docs/images/settings-general.png) | ![Stepping and OSD settings](docs/images/settings-stepping.png) |
| ![Hotkeys](docs/images/settings-hotkeys.png) | ![Monitors and live test](docs/images/settings-monitors.png) |

<p align="center">
  <img src="docs/images/osd-brightness.png" width="420" alt="Brightness overlay" />
  <img src="docs/images/osd-volume.png" width="420" alt="Volume overlay" />
</p>

## Troubleshooting

<details>
<summary><b>Brightness keys do nothing on my external monitor</b></summary>

- Turn on **DDC/CI** in the monitor's on-screen menu.
- Use HDMI, DisplayPort or USB-C directly from the GPU. Some docks, KVM switches and adapters block DDC/CI.
- Open **Monitors & Test** in LumiDisplay: if a monitor shows *N/A* for brightness, Windows cannot talk to it over DDC/CI.
</details>

<details>
<summary><b>Volume keys still change Windows volume</b></summary>

LumiDisplay only takes over the volume keys when **Intercept volume keys** is on *and* the selected monitor reports a speaker over DDC/CI. With **Forward to system audio** on (the default), the keys also stay with Windows whenever sound is playing through something other than a monitor, such as headphones or laptop speakers.
</details>

<details>
<summary><b>"Windows protected your PC" when starting</b></summary>

This is SmartScreen warning about an unsigned app downloaded from the internet. Click **More info → Run anyway**. You can verify the download against `SHA256SUMS.txt` on the release page.
</details>

<details>
<summary><b>A shortcut conflicts with another app</b></summary>

Shortcuts are registered system-wide, so only one app can own a combination. When another app already uses one of yours, LumiDisplay shows a notification and marks that shortcut red in **Settings → Hotkeys** — click it and pick different keys.
</details>

## Uninstall

1. In LumiDisplay, turn off **Start with Windows**, then click **Quit LumiDisplay**.
2. Delete `LumiDisplay.exe`.
3. Optionally delete your settings folder: `%LocalAppData%\LumiDisplay`.

## Build from source

<details>
<summary>Instructions</summary>

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
git clone https://github.com/andikatahang/LumiDisplay.git
cd LumiDisplay
dotnet build src/LumiDisplay/LumiDisplay.csproj
```

Portable single-file build (the same as the release):

```bash
dotnet publish src/LumiDisplay/LumiDisplay.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o publish/
```

Pushing a tag like `v1.2.0` builds and publishes a GitHub Release automatically (`.github/workflows/release.yml`).

Developer flags:
- `--autostart` — what the Windows sign-in entry runs; stays in the tray when *Stay in tray at sign-in* is on.
- `--export-icon <path>` — regenerates `lumidisplay.ico` from the logo renderer in `Theme.cs`.
</details>

## How it works

<details>
<summary>Architecture</summary>

| Part | Implementation | Source |
|---|---|---|
| External monitors | DDC/CI via `dxva2.dll` (`SetVCPFeature` / `GetVCPFeatureAndVCPFeatureReply`) — VCP `0x10` brightness, `0x12` contrast, `0x62` volume, `0x8D` mute | [`DdcMonitor.cs`](src/LumiDisplay/DdcMonitor.cs) |
| Laptop screens | WMI `root\wmi` — `WmiMonitorBrightness` / `WmiSetBrightness` | [`WmiMonitor.cs`](src/LumiDisplay/WmiMonitor.cs) |
| Volume keys | Low-level keyboard hook (`WH_KEYBOARD_LL`) | [`MediaKeyInterceptor.cs`](src/LumiDisplay/MediaKeyInterceptor.cs) |
| Shortcuts | `RegisterHotKey` | [`HotkeyManager.cs`](src/LumiDisplay/HotkeyManager.cs) |
| On-screen display | Per-pixel-alpha layered window (`UpdateLayeredWindow`) | [`OsdOverlay.cs`](src/LumiDisplay/OsdOverlay.cs) |
| UI | WinForms with custom-drawn controls, light theme | [`SettingsForm.cs`](src/LumiDisplay/SettingsForm.cs), [`ThemedControls.cs`](src/LumiDisplay/ThemedControls.cs), [`Theme.cs`](src/LumiDisplay/Theme.cs) |
| Settings | JSON in `%LocalAppData%\LumiDisplay\settings.json` | [`AppSettings.cs`](src/LumiDisplay/AppSettings.cs) |

Stepping follows BetterDisplay on macOS: 64 logical steps, 4 per OSD chiclet (6.25% per press, 1.56% for fine steps).
</details>

## License

[MIT](LICENSE). LumiDisplay is an independent Windows app inspired by [BetterDisplay](https://github.com/waydabber/BetterDisplay) for macOS; it is not affiliated with it.
