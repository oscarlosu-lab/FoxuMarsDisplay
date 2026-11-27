# FoxuMarsDisplay — Development Workflow & Roadmap

**English** | *Resumen en Español incluido en cada fase*

This document tracks the engineering workflow, quality gates, and release milestones for **FoxuMarsDisplay**.

---

## 1. Engineering Workflow & Quality Gates

Every feature or fix passes four mandatory gates before release:

1. **Build Gate**: `dotnet build FoxuMarsDisplay.slnx -c Release` must complete with `0` errors across all projects (`Core`, `Cli`, `FoxuMarsDisplay`, `Launcher`, `Setup`).
2. **Test Gate**: `dotnet test tests/FoxuMarsDisplay.Core.Tests/FoxuMarsDisplay.Core.Tests.csproj -c Release` must achieve `100%` pass rate (unit, protocol, theme parser, config, i18n, and renderer tests).
3. **Hardware & Soak Verification Gate**: Dual-screen stability test targeting `< 1%` CPU, low RAM footprint, and `0` send errors.
4. **Release & Security Gate**: Clean-source verification, `.\scripts\Build-Installer.ps1` producing `FoxuMarsDisplay_Setup.exe`, and SHA-256 checksums.

---

## 2. Completed Milestones

### ✅ v1.0.x – v1.1.0 — Cyber-HUD Engine & Universal Theme Compatibility
- Ultra-lightweight SkiaSharp renderer, SOEYI `.photo` & TURZX `.smtheme` / `theme.yaml` v2/v3 parser, zero-UAC permanent installer, 5-language auto-detection, radial/graph widgets, and automatic `180°` vertical panel calibration.

### ✅ v2.0.0 — Unicorn Engine
- Native Windows Media Foundation `.mp4` background decoder with `.sdac` WebP frame caching.
- Full `Setting.txt` & `foxu-theme-v1` JSON widget parity (`BorderLine`, `GridLine`, `RingProgressBar`, `AudioVisualizerControl`, `SongIcon`, `SonglyricsControl`).
- Multi-protocol USB catalog & backends (MacroSilicon MS9132/MS912C, HL-VMAX/SOEYI CDC, ArtInChip, TURZX Serial, Thermalright/ChiZhu/MiraBox), WASAPI 16-band FFT, LRCLIB synced lyrics, `FOXU // STUDIO` WYSIWYG editor, auto game/idle profile switching, and GitHub Releases `UpdateChecker`.

### ✅ v2.1.1 — Studio Rich-Text Editor, Color Palette & Manual Weather Location
- **Manual Weather Location:** Open-Meteo Geocoding city search in Sistema tab with Auto (IP) fallback and `WEATHER_CITY` sensor token.
- **Rich-Text `FOXU // STUDIO`:** Virtualized font picker with live typeface preview, dedicated font size controls, Bold/Italic/Underline/Strikeout/Shadow, Left/Center/Right alignment + 1-click canvas centering, 48-swatch RGBA color palette dropdown, `(Ninguno — Texto fijo)` sensor option, and direct `✏️ Edit Theme` button per screen card.

### ✅ v2.1.0 — Clean-Room Engine & Hardware Expansion
- **Serial Tile Differential Engine (`SerialTileDiffEngine`):** 16×16 dirty-rectangle differ over direct Win32 serial handles with byte-budget pacing (`98.78%` bandwidth savings in 100-frame benchmark).
- **Native WinUSB SLV3 Backend (`TurzxWinUsbBackend`):** Direct SetupAPI + WinUSB transport with DES-CBC 512-byte command framing and expanded USB display catalog.
- **Enhanced Media, Night Schedule & Canvas Scaler:** `LyricsOffsetMs` (`-5000..+5000 ms`), browser title cleanup, Deezer album-art fallback, radial dominant-color ambient glow, automatic day/night `BrightnessSchedule`, and proportional widget scaling in `FOXU // STUDIO`.
- **Local Web/REST Remote Control (`LocalHttpRemoteServer`):** Loopback HTTP server (`127.0.0.1:47821`) with dark Web UI, REST endpoints, and optional Bearer token authentication.

---

## 3. Future Backlog (v2.2.0+)
- **Additional Native Backends:** Direct HID/USB drivers for newly cataloged AIO and GPU edge displays.
- **Per-Screen Slideshow UI:** Playlist editor and shuffle controls in the main dashboard (backend `SlideshowEnabled` / `SlideshowIntervalSec` already supported in `config.json`).
- **Automated Layout Import/Export Presets:** One-click horizontal ↔ vertical layout adaptation templates in `FOXU // STUDIO`.
