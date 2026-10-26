# LumiDisplay & BetterDisplay Reverse Engineering — Project Handoff

> **Update (v1.1.0):** source code now lives in `src/LumiDisplay/` (build with `dotnet build src/LumiDisplay/LumiDisplay.csproj`).
> The UI moved from the Obsidian Dark theme described below to a light theme (white / soft gray / dark blue `#1E3A8A`) — see `Theme.cs`, `ThemedControls.cs` and the README.
> Volume keys are only taken over for monitors with a DDC/CI speaker (`AudioEndpoint.cs` + `TrayApplicationContext.GetVolumeKeyTargets`).

> **Target Audience:** Next AI Model, Engineer, or Contributor resuming this project.  
> **Date:** October 2026  
> **Author:** Andika (`andika@users.noreply.github.com`)  
> **Status:** Production v1.0.0 released | Reverse Engineering Completed | Windows Native Port Fully Functional

---

## 1. Executive Summary

This project accomplished two major milestones:
1. **Full Static Reverse Engineering of BetterDisplay v5.1.1 (macOS)**: Decoded the complete ARM64 Mach-O binary (50,380 functions) to understand how BetterDisplay captures media keys, orchestrates DDC/CI hardware communication, interacts with Apple Silicon/Intel display controllers, and renders its iconic 16-chiclet translucent OSD.
2. **Clean-Room Windows Native Equivalent (`LumiDisplay`)**: Built an entirely native, high-performance Windows 10/11 application using .NET 8 (WinForms + Win32 interop) that replicates BetterDisplay's core feature set with a modern **Obsidian Dark** aesthetic and **Dynamic Multi-Accent** palette.

---

## 2. Workspace Organization & Repositories

The project is split into two independent repositories under `/mnt/d/Proyek/` (WSL Linux path accessing Windows drive `D:\Proyek\`):

```
/mnt/d/Proyek/
├── BetterDisplay-ReverseEngineer/  [Local Research Repo]
│   ├── BetterDisplay-v5.1.1.dmg     # Original macOS installer (Apple disk image)
│   ├── REVERSE_ENGINEERING_REPORT.md# Exhaustive 500+ line technical reverse engineering report
│   ├── disasm.py                    # Capstone ARM64 disassembler with Mach-O binding annotations
│   ├── xref.py                      # ADRP/LDR cross-reference scanner for ARM64 binaries
│   ├── hfs_extract.py / hfs_full.py # Pure-Python HFS+ catalog and file tree extractors
│   ├── objc.py / swiftmeta.py       # Objective-C / Swift type metadata parsers
│   ├── PROJECT_HANDOFF.md           # This handoff document
│   └── README.md                    # Research overview documentation
│
└── LumiDisplay/                    [Production App Repo -> git@github.com:andikatahang/LumiDisplay.git]
    ├── .github/workflows/           # CI/CD: automated release build on tag push (v*)
    ├── LumiDisplay.csproj           # .NET 8 WinForms project definition
    ├── Program.cs                   # Application entry point, DPI awareness & Mutex
    ├── TrayApplicationContext.cs    # System tray lifecycle, dark menu renderer & monitor poller
    ├── DdcMonitor.cs                # Low-level DDC/CI driver via dxva2.dll
    ├── WmiMonitor.cs                # WMI backlight controller for built-in laptop panels
    ├── IMonitor.cs                  # Common display controller interface
    ├── MediaKeyInterceptor.cs       # WH_KEYBOARD_LL low-level keyboard hook
    ├── HotkeyManager.cs             # Global keyboard shortcut manager (RegisterHotKey)
    ├── OsdOverlay.cs                # Layered WinForms translucent glass OSD (16 chiclets)
    ├── SettingsForm.cs              # Immersive Dark Mode settings & live-test dialog
    ├── AppSettings.cs               # JSON configuration model (%LocalAppData%\LumiDisplay\)
    ├── StartupManager.cs            # Windows registry autostart manager (HKCU\...\Run)
    ├── lumidisplay.ico              # Multi-resolution icon (16, 32, 48, 64px)
    ├── publish/                     # Pre-compiled binaries (single-file x64 executable & zip)
    ├── LICENSE                      # MIT License
    └── README.md                    # User and developer documentation in English
```

---

## 3. Reverse Engineering Findings & Architecture Mapping

### BetterDisplay (macOS) vs. LumiDisplay (Windows)

| Functional Area | BetterDisplay v5.1.1 (macOS) | LumiDisplay (Windows) | Implementation File |
|---|---|---|---|
| **External DDC/CI** | `IOAVServiceWriteI2C` / Framebuffer `IOConnectCallMethod` | `dxva2.dll` (`SetVCPFeature`, `GetVCPFeatureAndVCPFeatureReply`) | `LumiDisplay/DdcMonitor.cs` |
| **Built-in Laptop Screen** | Private framework `DisplayServicesSetBrightness` | WMI `root\wmi: WmiMonitorBrightnessMethods.WmiSetBrightness` | `LumiDisplay/WmiMonitor.cs` |
| **Media Key Capture** | `CGEventTapCreate` (passive/active tap for `NX_KEYTYPE_*`) | Win32 `SetWindowsHookEx(WH_KEYBOARD_LL)` | `LumiDisplay/MediaKeyInterceptor.cs` |
| **Custom Hotkeys** | Carbon `RegisterEventHotKey` | Win32 `RegisterHotKey` (user32.dll) | `LumiDisplay/HotkeyManager.cs` |
| **Visual Feedback (OSD)** | `GlassOSD` / `AppOSD` (16 chiclets) | WinForms layered window (`WS_EX_LAYERED`, `WS_EX_TRANSPARENT`) | `LumiDisplay/OsdOverlay.cs` |
| **Stepping Arithmetic** | 64 logical steps, 4 steps per chiclet | 64 logical steps, 4 per chiclet (standard: 6.25%, fine: 1.56%) | `LumiDisplay/AppSettings.cs` |
| **Settings Storage** | `UserDefaults` (`~/Library/Preferences/...`) | `%LocalAppData%\LumiDisplay\settings.json` | `LumiDisplay/AppSettings.cs` |

### VCP Codes (VESA MCCS Standard)
Identical across both operating systems:
- `0x10`: Luminance (Brightness)
- `0x12`: Contrast
- `0x62`: Audio Speaker Volume
- `0x8D`: Audio Mute (`1` = Mute, `2` = Unmute)

---

## 4. LumiDisplay Subsystem Details

### 4.1. Hardware Control Pipeline (`DdcMonitor.cs` & `WmiMonitor.cs`)
- **DDC/CI:** Uses `EnumDisplayMonitors` ➔ `GetNumberOfPhysicalMonitorsFromHMONITOR` ➔ `GetPhysicalMonitorsFromHMONITOR`. Implements `IDisposable` and a safe finalizer calling `DestroyPhysicalMonitor` to prevent resource leaks.
- **WMI:** Queries `root\wmi` using `ManagementObjectSearcher` for `WmiMonitorBrightness`. Updates brightness via `InvokeMethod("WmiSetBrightness", ...)`. Includes timeout and null-safe COM cleanup.
- **Unified Interface:** `IMonitor` unifies both display types so the rest of the app treats external monitors and laptop screens identically.

### 4.2. Low-Level Keyboard Interception (`MediaKeyInterceptor.cs`)
- Installs a thread-safe `WH_KEYBOARD_LL` hook capturing `VK_VOLUME_UP` (`0xAF`), `VK_VOLUME_DOWN` (`0xAE`), and `VK_VOLUME_MUTE` (`0xAD`).
- When external displays with audio capabilities are active, it consumes the event (`return (IntPtr)1`) and sends DDC/CI commands, preventing Windows from adjusting the wrong audio device.
- If no monitor audio is detected or if forwarding is enabled in settings, it allows the event to pass through (`CallNextHookEx`).

### 4.3. macOS-Style Glass OSD (`OsdOverlay.cs`)
- Uses Win32 extended window styles `WS_EX_LAYERED`, `WS_EX_TRANSPARENT`, `WS_EX_TOOLWINDOW`, and `WS_EX_TOPMOST`.
- Renders 16 chiclets with a reactive glow and smooth 1.5-second opacity fade-out.
- **Dynamic Multi-Accent Colors:**
  - ☀️ **Solar Gold** (`#F59E0B`) for Brightness
  - 🌊 **Electric Cyan** (`#06B6D4`) for Volume
  - 🔴 **Neon Coral** (`#EF4444`) for Mute
  - 🔮 **Radiant Violet** (`#A855F7`) for Contrast
  - Background: **Obsidian Glass** (`rgba(18, 16, 26, 0.86)`)

### 4.4. Modern UI & Settings (`SettingsForm.cs` & `TrayApplicationContext.cs`)
- **Immersive Dark Titlebar:** Invokes `DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, 1)`.
- **Custom Context Menu:** `LumiDisplayMenuRenderer` overrides `ProfessionalColorTable` with Obsidian dark surfaces (`#14121E`), subtle borders (`#3E3658`), and cyan hover accents (`#06B6D4`).
- **Owner-Drawn Tab Control:** Eliminates WinForms standard light gray tabs in favor of a dark surface with an active cyan indicator line.

---

## 5. Development Environment & Build Tooling

### SDK & Toolchain
- **OS:** Linux (Ubuntu under WSL2) cross-compiling to Windows `win-x64`.
- **.NET SDK:** .NET 8.0 SDK installed at `/home/andika/.dotnet/dotnet`.
- **Cross-Targeting Flag:** In `LumiDisplay.csproj`, `<EnableWindowsTargeting>true</EnableWindowsTargeting>` allows building `net8.0-windows` on Linux without a Windows host.

### Build Commands
```bash
cd /mnt/d/Proyek/LumiDisplay

# Standard Debug Build
/home/andika/.dotnet/dotnet build

# Standalone Single-File Release (Self-contained, compressed ~65 MB)
/home/andika/.dotnet/dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o publish/
```

### Git & Remote Configuration
- **LumiDisplay Remote:** `git@github.com:andikatahang/LumiDisplay.git`
- **Branch:** `main`
- **SSH Key:** `~/.ssh/id_ed25519` (configured in `~/.ssh/config` for `github.com`)
- **Author Identity:** `Andika <andika@users.noreply.github.com>`

---

## 6. Current Implementation State

- [x] Full static reverse engineering of BetterDisplay v5.1.1 (`REVERSE_ENGINEERING_REPORT.md`).
- [x] Dual-controller display engine (DDC/CI + WMI).
- [x] Low-level media key hook with conditional forwarding.
- [x] Global hotkeys (Standard & fine-scale brightness/contrast).
- [x] 16-chiclet glass OSD with reactive glow and multi-accent colors.
- [x] Obsidian Dark theme applied to Settings Form and Tray Menu.
- [x] Single-file portable executable published and compressed to ~65 MB.
- [x] GitHub repository initialized, committed, and pushed.
- [x] GitHub Actions CI/CD workflow created (`.github/workflows/release.yml`).
- [x] Tag `v1.0.0` pushed to GitHub trigger automated release.
- [x] Polished English `README.md` and `LICENSE` (MIT).

---

## 7. Recommended Future Roadmap

For the next engineer or AI model picking up this codebase, here are the most impactful future features to consider:

1. **Virtual Display / Dummy Plug Driver:**
   - BetterDisplay on macOS has a popular feature creating virtual displays (dummy plugs) with arbitrary resolutions and HiDPI scaling.
   - On Windows, this can be implemented via the **IddSampleDriver** (Indirect Display Driver - IDD) to allow headless streaming, custom virtual resolutions, and virtual multi-monitor setups.
2. **Software Dimming Below Hardware Minimum (Gamma Table / Dimmer Overlay):**
   - When a hardware monitor reaches 0% brightness, BetterDisplay continues dimming by reducing software gamma ramps or applying a transparent dark overlay.
   - On Windows, this can be implemented using `SetDeviceGammaRamp` (gdi32.dll) or a persistent transparent click-through layered window.
3. **Smooth Stepping Transitions (Fluid Interpolation):**
   - Add a high-resolution timer (e.g., 60 FPS) that smoothly animates brightness changes between steps rather than jumping instantaneously.
4. **Per-Monitor Custom Response Curves:**
   - Allow users to map nonlinear brightness curves (logarithmic/perceptual) per monitor to match display perceptions across different manufacturers (e.g., Dell vs. LG).
5. **Windows Installer (Inno Setup / MSIX / Winget):**
   - Package `LumiDisplay.exe` into an Inno Setup installer or submit a manifest to Microsoft Winget (`winget install LumiDisplay`).

---

## 8. Important Gotchas & Caveats

1. **DDC/CI Quirks on Certain GPUs / Cables:**
   - Some HDMI cables or specific GPU driver settings (especially older AMD/Intel integrated drivers) disable DDC/CI communication. Always check monitor OSD menu to ensure DDC/CI is toggled **ON**.
2. **Administrator Rights for Startup Registry:**
   - Startup registration uses `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. This does **not** require elevation (UAC prompt), which is intentional for a seamless user experience. Do not move this to `HKLM`.
3. **Single-Instance Mutex:**
   - The app uses a named system mutex `LumiDisplay_SingleInstance`. If debugging multiple instances, ensure the previous process has terminated cleanly.
4. **Cross-Compilation from Linux:**
   - Always run dotnet commands using `/home/andika/.dotnet/dotnet` and ensure `<EnableWindowsTargeting>true</EnableWindowsTargeting>` remains in `LumiDisplay.csproj`.
