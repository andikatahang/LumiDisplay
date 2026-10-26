# LumiDisplay

<p align="center">
  <img src="lumidisplay.ico" width="80" height="80" alt="LumiDisplay Logo" />
</p>

<p align="center">
  <strong>Intelligent external display and laptop brightness, contrast, and audio volume control for Windows.</strong><br>
  <em>A clean-room native port reverse engineered from macOS's acclaimed BetterDisplay.</em>
</p>

<p align="center">
  <a href="https://github.com/andikatahang/LumiDisplay/releases"><img src="https://img.shields.io/github/v/release/andikatahang/LumiDisplay?color=06B6D4&label=release" alt="GitHub Release"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-F59E0B.svg" alt="License: MIT"></a>
  <img src="https://img.shields.io/badge/platform-Windows%2010%20%7C%2011%20(x64)-A855F7.svg" alt="Platform">
  <img src="https://img.shields.io/badge/.NET-8.0-blueviolet.svg" alt=".NET 8.0">
</p>

---

> **Notice:**  
> This project was developed through rigorous **reverse engineering** of **BetterDisplay v5.1.1 (macOS)**, faithfully recreating its dual display controller architecture, low-level media key interception, 16-chiclet stepping logic, and glass visual feedback natively for **Windows 10 & 11**.

---

## 🌟 Overview

On macOS, utilities like **BetterDisplay** provide an unmatched experience for seamlessly controlling third-party monitor hardware parameters (DDC/CI) directly using Apple keyboard media keys and shortcuts. 

Windows historically lacked an equivalent, polished solution that unified external monitor speaker control, built-in laptop screen dimming, and hardware DDC brightness with an elegant macOS-inspired on-screen display.

**LumiDisplay** bridges this gap: a lightweight, high-performance native Windows application built on .NET 8 WinForms and Win32 Interop, featuring a clean **light theme** — white and soft-gray surfaces with a **dark blue** accent.

---

## 🚀 Key Features

- **Dual Controller Architecture (Matching BetterDisplay)**
  - **External Displays**: Direct hardware DDC/CI control via `dxva2.dll` (VCP `0x10` Luminance/Brightness, `0x12` Contrast, `0x62` Speaker Volume, `0x8D` Mute).
  - **Built-in Laptop Panels**: Windows Management Instrumentation (WMI `root\wmi: WmiMonitorBrightness` / `WmiSetBrightness`), equivalent to BetterDisplay's `AppleBuiltInDirectController`.
- **Native Media Key Interception**
  - Seamlessly intercepts keyboard `Volume Up`, `Volume Down`, and `Mute` keys via a low-level keyboard hook (`WH_KEYBOARD_LL`).
  - Routes volume adjustments directly to external monitor hardware speakers via DDC/CI.
  - Automatically falls back to native Windows system audio when active output devices do not support hardware volume.
- **Custom Global Hotkeys**
  - `Ctrl + Alt + Up / Down`: Standard brightness adjustment (±1 chiclet / 6.25%).
  - `Ctrl + Alt + Shift + Up / Down`: Fine-scale brightness adjustment (±0.25 chiclet / 1.56%).
  - `Ctrl + Alt + Right / Left`: Contrast adjustment.
- **macOS-Style 16-Chiclet OSD**
  - Floating white card with a soft drop shadow at the bottom-center of the screen, rendered with per-pixel alpha (`UpdateLayeredWindow`).
  - 16 dark blue (`#1E3A8A`) chiclets, caption and percentage; a red speaker marks mute.
  - Smooth fade-out after ~1.5s.
- **Light Theme UI & System Tray**
  - Palette: white `#FFFFFF`, soft gray `#F4F6FA` / `#E2E8F0`, dark blue accent `#1E3A8A` (see `Theme.cs`).
  - Custom tray context menu (`LumiDisplayMenuRenderer`) with line icons and dark blue check marks.
  - Instant quick-presets (0%, 25%, 50%, 75%, 100%).
  - Synchronized multi-monitor control or individual display targeting.
  - Live hardware capability inspector.
- **Settings Window**
  - Sidebar navigation (General, Stepping & OSD, Hotkeys, Monitors & Test), white cards, toggle switches, key-cap shortcut list and live sliders.
  - White Windows 11 caption bar; `Ctrl+Tab` / `Ctrl+1…4` switch pages, `Esc` closes.
  - Configurable logical steps with a live 16-chiclet step preview.
  - Automatic startup on Windows login option via Registry (`HKCU\...\Run`).

---

## 📊 Architectural Mapping: BetterDisplay (macOS) vs LumiDisplay (Windows)

| Component | BetterDisplay (macOS) | LumiDisplay (Windows) |
|---|---|---|
| **External DDC/CI** | `IOAVServiceWriteI2C` / Framebuffer `IOConnectCallMethod` | `dxva2.dll` (`SetVCPFeature`, `GetVCPFeatureAndVCPFeatureReply`) |
| **Built-in Laptop Panel** | Private `DisplayServicesSetBrightness` | `root\wmi: WmiMonitorBrightnessMethods.WmiSetBrightness` |
| **Media Key Interception** | `CGEventTapCreate` (passive/active tap) | `SetWindowsHookEx(WH_KEYBOARD_LL)` |
| **Custom Shortcuts** | Carbon `RegisterEventHotKey` | Win32 `RegisterHotKey` (user32.dll) |
| **Visual Feedback** | `GlassOSD` / `AppOSD` (16 chiclets) | Per-pixel-alpha layered WinForms OSD (16 chiclets) |
| **Stepping Calculation** | 64 total steps, 4 steps per chiclet | 64 total steps, 4 steps per chiclet (fully configurable) |
| **Standard VCP Codes** | `0x10` (Bright), `0x12` (Contr), `0x62` (Vol), `0x8D` (Mute) | `0x10` (Bright), `0x12` (Contr), `0x62` (Vol), `0x8D` (Mute) |

---

## ⌨️ Default Keyboard Shortcuts

| Shortcut | Action | Step Size |
|---|---|---|
| <kbd>Volume Up</kbd> | Increase Monitor Speaker Volume | Standard Step (4 steps / 1 chiclet) |
| <kbd>Volume Down</kbd> | Decrease Monitor Speaker Volume | Standard Step (4 steps / 1 chiclet) |
| <kbd>Volume Mute</kbd> | Toggle Monitor Speaker Mute | Immediate Toggle (VCP `0x8D`) |
| <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>↑</kbd> | Increase Display Brightness | Standard Step (6.25% / 1 chiclet) |
| <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>↓</kbd> | Decrease Display Brightness | Standard Step (6.25% / 1 chiclet) |
| <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>Shift</kbd> + <kbd>↑</kbd> | Fine Brightness Up | Fine Step (1.56% / 0.25 chiclet) |
| <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>Shift</kbd> + <kbd>↓</kbd> | Fine Brightness Down | Fine Step (1.56% / 0.25 chiclet) |
| <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>→</kbd> | Increase Contrast | Standard Step |
| <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>←</kbd> | Decrease Contrast | Standard Step |

---

## 📦 Download & Installation

### Option 1: Pre-Built Binary (Recommended)
1. Download the latest release from [**GitHub Releases**](https://github.com/andikatahang/LumiDisplay/releases).
2. Extract `LumiDisplay-win-x64.zip` and run `LumiDisplay.exe`.
3. Zero dependencies required — runs out-of-the-box on any 64-bit Windows 10/11 system without installing .NET runtimes.

### Option 2: Build from Source

#### Prerequisites
- Windows 10 (version 1809+) or Windows 11 (x64)
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

```bash
# Clone the repository
git clone https://github.com/andikatahang/LumiDisplay.git
cd LumiDisplay

# Build debug version
dotnet build

# Publish portable single-file executable
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o publish/
```

The resulting standalone executable will be generated at `publish/LumiDisplay.exe`.

#### Developer flags
- `LumiDisplay.exe --settings` opens the settings window right after launch.
- `LumiDisplay.exe --export-icon lumidisplay.ico` regenerates the multi-resolution icon (16–256px) from the logo renderer in `Theme.cs`.

---

## 🎯 Usage

1. Launch `LumiDisplay.exe`.
2. The application will start minimized to the **System Tray** (white monitor with a sun on a dark blue tile).
3. Use your physical keyboard keys to adjust brightness, contrast, and audio volume.
4. **Right-click the tray icon** to:
   - Apply instant percentage presets (0% to 100%).
   - Select specific target monitors or toggle synchronized multi-monitor control.
   - Inspect connected display capabilities and DDC/CI communication status.
   - Open **Settings…**.
5. **Double-click the tray icon** to directly open the Settings dialog.

---

## 📁 Project Architecture

```
LumiDisplay/
├── .github/workflows/        # Automated CI/CD pipeline for GitHub Releases
├── LumiDisplay.csproj        # .NET 8 WinForms Windows project definition
├── Program.cs                # Entry point, DPI awareness & single-instance mutex
├── TrayApplicationContext.cs # System tray context, menu renderer & orchestrator
├── DdcMonitor.cs             # Low-level DDC/CI driver via dxva2.dll
├── WmiMonitor.cs             # WMI driver for built-in laptop panels
├── IMonitor.cs               # Display controller abstraction interface
├── MediaKeyInterceptor.cs    # Low-level WH_KEYBOARD_LL hook for media keys
├── HotkeyManager.cs          # Win32 global hotkey manager
├── OsdOverlay.cs             # macOS-style OSD overlay (16 chiclets, per-pixel alpha)
├── SettingsForm.cs           # Light theme settings window with sidebar navigation
├── Theme.cs                  # Palette, line glyphs and the logo renderer
├── ThemedControls.cs         # Toggle switch, slider, cards, key caps and other custom controls
├── IconExporter.cs           # Writes lumidisplay.ico from the logo renderer
├── AppSettings.cs            # JSON configuration persistence model
├── StartupManager.cs         # Windows registry autostart manager
├── lumidisplay.ico           # Multi-resolution application icon
├── LICENSE                   # MIT License
└── README.md                 # Project documentation
```

---

## ⚖️ License & Attribution

- Released under the [MIT License](LICENSE).
- Core DDC/CI hardware control concepts, media key interception workflows, and 16-chiclet stepping logic are derived from clean-room static analysis of **BetterDisplay** (macOS).
- All Windows implementation code is 100% original, authored for .NET 8 and Win32.
