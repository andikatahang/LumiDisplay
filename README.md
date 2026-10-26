# LumiDisplay

> **Catatan Asal-Usul Proyek / Project Notice:**  
> Proyek ini merupakan hasil **reverse engineering** dari aplikasi **BetterDisplay** versi macOS (v5.1.1), yang kemudian diimplementasikan dan dibangun ulang secara native untuk sistem operasi Windows dengan nama **LumiDisplay**.  
>  
> *This software is a clean-room native Windows equivalent born from reverse engineering **BetterDisplay v5.1.1 (macOS)**, faithfully recreating its dual display controller architecture, media key interception, 16-chiclet stepping logic, and visual feedback for Windows 10 & 11.*

---

## 🌟 Tentang LumiDisplay

Pada macOS, **BetterDisplay** adalah standar emas untuk mengatur kecerahan, kontras, serta volume monitor eksternal melalui DDC/CI dan panel bawaan laptop. Namun, Windows tidak memiliki utilitas bawaan yang setara, mulus, dan terintegrasi dengan tombol keyboard multimedia.

**LumiDisplay** hadir mengisi celah tersebut dengan mereplikasi seluruh alur arsitektur BetterDisplay secara murni di platform Windows (.NET 8 WinForms & Win32 Interop), dibalut antarmuka modern **Obsidian Dark** dan aksen multi-warna dinamis.

---

## 🚀 Fitur Utama

- **Dual Controller Architecture (Sesuai BetterDisplay)**
  - **Monitor Eksternal**: DDC/CI hardware control via `dxva2.dll` (VCP `0x10` Luminance, `0x12` Contrast, `0x62` Speaker Volume, `0x8D` Mute).
  - **Layar Bawaan Laptop**: Windows Management Instrumentation (WMI `root\wmi: WmiMonitorBrightness` / `WmiSetBrightness`), setara dengan `AppleBuiltInDirectController` pada BetterDisplay.
- **Intersepsi Tombol Media Native**
  - Mencegat tombol keyboard `Volume Up`, `Volume Down`, dan `Mute` via Low-Level Keyboard Hook (`WH_KEYBOARD_LL`).
  - Mengarahkan pengaturan volume langsung ke speaker monitor eksternal via DDC/CI.
  - Otomatis meneruskan ke audio Windows bila monitor tidak mendukung kontrol volume.
- **Global Hotkeys Kustom**
  - `Ctrl + Alt + Up / Down`: Pengaturan Brightness standard step (±1 chiclet / 6.25%).
  - `Ctrl + Alt + Shift + Up / Down`: Pengaturan Brightness fine step (±0.25 chiclet / 1.56%).
  - `Ctrl + Alt + Right / Left`: Pengaturan Contrast standard step.
- **macOS-Style Glass OSD Overlay (16 Chiclets)**
  - Tampilan overlay mengambang transparan (Obsidian Glass) di bagian bawah layar.
  - 16 chiclets dengan transisi fade-out halus (~1.5 detik).
  - **Dynamic Multi-Accent Colors**:
    - ☀️ **Solar Gold** (`#F59E0B`) — Brightness
    - 🌊 **Electric Cyan** (`#06B6D4`) — Volume
    - 🔴 **Neon Coral** (`#EF4444`) — Audio Mute
    - 🔮 **Radiant Violet** (`#A855F7`) — Contrast
- **System Tray & Dark Context Menu**
  - Berjalan hening di system tray dengan menu bertema gelap penuh (`LumiDisplayMenuRenderer`).
  - Preset instan (0%, 25%, 50%, 75%, 100%).
  - Sinkronisasi multi-monitor atau pengaturan per-monitor.
  - Monitor inspector untuk memeriksa kapabilitas hardware DDC/CI & WMI.
- **Jendela Pengaturan Modern (Immersive Dark Mode)**
  - Mendukung Windows 11/10 Immersive Dark Titlebar (`DWMWA_USE_IMMERSIVE_DARK_MODE`).
  - Konfigurasi total steps, standard step, fine step, dan default scale.
  - Slider pengujian langsung (live test) untuk kecerahan dan volume.
  - Integrasi startup otomatis Windows via Registry (`HKCU\...\Run`).

---

## 📊 Perbandingan Arsitektur: BetterDisplay (macOS) vs LumiDisplay (Windows)

| Komponen | BetterDisplay (macOS) | LumiDisplay (Windows) |
|---|---|---|
| **External Monitor DDC** | `IOAVServiceWriteI2C` / Framebuffer `IOConnectCallMethod` | `dxva2.dll` (`SetVCPFeature`, `GetVCPFeatureAndVCPFeatureReply`) |
| **Built-in Laptop Screen** | Private framework `DisplayServicesSetBrightness` | `root\wmi: WmiMonitorBrightnessMethods.WmiSetBrightness` |
| **Media Key Interception** | `CGEventTapCreate` (passive / active tap) | `SetWindowsHookEx(WH_KEYBOARD_LL)` |
| **Custom Shortcuts** | Carbon `RegisterEventHotKey` | Win32 `RegisterHotKey` (user32.dll) |
| **Visual Feedback** | `GlassOSD` / `AppOSD` (16 chiclets) | Layered WinForms translucent glass OSD (16 chiclets + multi-accent) |
| **Stepping Calculation** | 64 total steps, 4 steps per chiclet | 64 total steps, 4 steps per chiclet (dapat dikonfigurasi) |
| **MCCS VCP Codes** | `0x10` (Bright), `0x12` (Contr), `0x62` (Vol), `0x8D` (Mute) | `0x10` (Bright), `0x12` (Contr), `0x62` (Vol), `0x8D` (Mute) |

---

## 🛠️ Persyaratan Sistem & Kompilasi

### Persyaratan
- **OS:** Windows 10 (versi 1809+) atau Windows 11 (64-bit).
- **SDK:** .NET 8.0 SDK (hanya diperlukan jika ingin membangun dari source code).
- **Display:** Monitor eksternal dengan fitur DDC/CI aktif di menu OSD bawaan monitor (HDMI, DisplayPort, USB-C, atau DVI), atau laptop dengan layar internal yang didukung WMI.

### Membangun dari Source Code
```bash
# Clone atau masuk ke direktori LumiDisplay
cd LumiDisplay

# Debug build
dotnet build

# Publish executable tunggal mandiri (Self-Contained Single-File)
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish/
```

Hasil build mandiri akan berada di direktori `publish/LumiDisplay.exe` (dapat dijalankan langsung di komputer Windows manapun tanpa instalasi .NET runtime tambahan).

---

## 🎮 Cara Menjalankan

1. Jalankan `LumiDisplay.exe`.
2. Aplikasi akan otomatis berjalan dan diminimalkan ke **System Tray** (ikon monitor bernuansa Obsidian & Cyan di pojok kanan bawah taskbar).
3. **Gunakan keyboard Anda:**
   - Tekan tombol **Volume Up / Down / Mute** pada keyboard untuk mengatur audio speaker monitor.
   - Tekan **Ctrl + Alt + Up / Down** untuk menaikkan/menurunkan kecerahan monitor.
   - Tekan **Ctrl + Alt + Shift + Up / Down** untuk penyesuaian kecerahan halus (fine step).
   - Tekan **Ctrl + Alt + Right / Left** untuk mengatur kontras monitor.
4. **Klik kanan ikon tray** untuk menu cepat, ganti monitor target, atau pilih **⚙️ Preferences / Settings...** untuk konfigurasi lebih lanjut.
5. **Klik dua kali ikon tray** untuk langsung membuka jendela Pengaturan.

---

## 📁 Struktur Berkas Proyek

```
LumiDisplay/
├── LumiDisplay.csproj        # Spesifikasi proyek .NET 8 WinForms Windows
├── Program.cs                # Entry point & single-instance mutex
├── TrayApplicationContext.cs # System tray context, dark renderer & orchestrator
├── DdcMonitor.cs             # Driver DDC/CI low-level via dxva2.dll
├── WmiMonitor.cs             # Driver WMI untuk layar bawaan laptop
├── IMonitor.cs               # Interface abstraksi controller monitor
├── MediaKeyInterceptor.cs    # Low-level hook WH_KEYBOARD_LL media keys
├── HotkeyManager.cs          # Pengelola hotkey global Windows
├── OsdOverlay.cs             # macOS Glass OSD renderer (16 chiclets)
├── SettingsForm.cs           # Dialog pengaturan Immersive Dark Mode
├── AppSettings.cs            # Model serialisasi konfigurasi JSON
├── StartupManager.cs         # Pengelola auto-run startup Windows
├── lumidisplay.ico           # Ikon aplikasi multi-resolusi
└── README.md                 # Dokumentasi proyek LumiDisplay
```

---

## ⚖️ Lisensi & Atribusi

- Proyek ini dirilis di bawah lisensi **MIT**.
- Ide dasar kontrol hardware DDC/CI dan sistem stepping 16-chiclet didasarkan pada hasil reverse engineering terhadap aplikasi **BetterDisplay** versi macOS.
