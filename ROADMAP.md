# FoxuMarsDisplay — Development Workflow & Roadmap

**English** | *Resumen en Español incluido en cada fase*

This document tracks the active engineering workflow, quality gates, and prioritized backlog for **FoxuMarsDisplay** (`v1.0.0`+).

---

## 1. Engineering Workflow & Quality Gates

Every feature or fix passes four mandatory gates before release:

1. **Build Gate**: `dotnet build FoxuMarsDisplay.slnx -c Release` must complete with `0` errors across all 5 projects (`Core`, `Cli`, `FoxuMarsDisplay`, `Launcher`, `Setup`).
2. **Test Gate**: `dotnet test tests/FoxuMarsDisplay.Core.Tests/FoxuMarsDisplay.Core.Tests.csproj -c Release` must achieve `100%` pass rate (unit, theme parser, config, i18n, and renderer tests).
3. **Hardware Verification Gate**: Dual-screen USB stability test (`240×320` AIO + `360×960` vertical panel) targeting `< 1%` CPU, `~60 MB` RAM, and `0` USB send errors with 2 Hz keep-alive active.
4. **Release & Security Gate**: Clean-source verification, `.\scripts\Build-Installer.ps1` producing `FoxuMarsDisplay_Setup.exe`, and SHA-256 checksums.

---

## 2. Prioritized Pending Workstreams

### ✅ P0 — Automatic 180° Orientation Calibration for Vertical Panels *(Completed for v1.0.1)*
* **Status:** Implemented & verified (unit tests + upright preview rendering)
* **What changed:**
  - `MsDisplayBackend` detects `VMAXC` vertical chassis panels (`height > width`) and applies a `+180°` (`HardwareRotationOffset = 2`) hardware scan offset automatically.
  - `DisplayHost` performs a one-time `CalibrationVersion` migration on legacy `v1.0.0` profiles so existing `Rotation = 2` settings migrate cleanly to `Rotation = 0` without flipping the screen again.
  - `SkiaThemeRenderer.CopyPreviewPngBytes` renders upright live bezel previews from the logical canvas regardless of physical panel rotation.

---

### ✅ P1 — Native TURZX Theme Compatibility (`v1.1.0`) & USB-Serial Hardware (`v1.2.0`)
* **Status:** Phases 1, 2, 3 & 5 **Completed in `v1.1.0`** — Full technical specification in [docs/TURZX_COMPATIBILITY_PLAN.md](docs/TURZX_COMPATIBILITY_PLAN.md)
* **Completed (`v1.1.0`):**
  1. **Phase 1 — Full `theme.yaml` v2/v3 Parser:** Hierarchical indentation parser supporting `DISPLAY_WIDTH/HEIGHT/ORIENTATION`, `STATIC_IMAGES`, `STATIC_TEXT`, `TEXT` alignment, `BAR`/`GRAPH`, `RADIAL`, `LINE_GRAPH`, `SHOW` flag, and comma RGB colors.
  2. **Phase 2 — Smart Aspect-Ratio Scaler:** Automatic `Fit` aspect-preserving scaling with clean `#000000` letterbox when aspect ratio differs >22% (e.g., `480×1920` -> `240×320`).
  3. **Phase 3 — SkiaSharp Gauge, Graph & Extended Sensors:** Radial arc gauges, ring-buffer history line graphs, plus NVML VRAM clock/used/fan and DriveInfo disk usage/free GB sensors.
  4. **Phase 5 — Automated Tests:** Pixel-verified `SkiaThemeRenderer` & `UnifiedThemeLoader` regression tests.
* **Pending (`v1.2.0`):**
  - **Phase 4 — `TurzxSerialBackend`:** Native `IDisplayBackend` over USB CDC (`System.IO.Ports`) for TURZX Rev A / B / C / E (`3.5"`, `5"`, `8.8"`), plus official TURZX Windows `.theme` digit/needle sprites.

---

### ✅ P1 — Theme Maker Schema Parity (`v1.1.0`) & Next-Gen Theme Creator (`v1.2.0`)
* **Status:** Schema & Format Parity **Completed in `v1.1.0`**; Integrated Creator in `v1.2.0`
* **Completed (`v1.1.0`):**
  - Full native parsing of Theme Maker / [**SnakeMarsTheme**](https://github.com/Snakefoxu/marsmakertheme) `.photo`, `.smtheme`, `Setting.txt` (`Text`, `BorderLine`, `DefaultLine`, segmented `GridLine`, `DisplayDirection`), and Type 0/1 `.json` (`DisplayTexts` + `DisplayImages`).
  - Complete `*PU` and extended sensor parity (`CPUTempPU`, `GpuUsagePU`, `GPUMemoryFrequencyPU`, `UpNetSpeed`, `DownNetSpeed`, `DiskUsage`/`DiskFree`, etc.).
  - Live Editor ↔ Engine Bridge via Named Pipe (`FoxuMarsDisplay_IPC_v1`).
* **Pending (`v1.2.0`):**
  - Next-Generation Integrated WYSIWYG Theme Creator (drag-and-drop canvas, live telemetry binding, animated GIF/WebP timeline preview, 1-click `.photo` / `.smtheme` export).

---

### 🟡 P2 — Video Wallpapers, Per-Screen Slideshow UI & CI/CD
* **MP4 / WebM Hardware-Decoded Wallpapers:** Pre-cropped `.sdac` frame caching for video backgrounds.
* **Per-Screen Slideshow Controls:** UI controls for interval, theme playlist, and shuffle (backend `SlideshowEnabled` / `SlideshowIntervalSec` already supported in `config.json`).
* **GitHub Actions CI/CD:** Automated `.NET 8` build, unit test execution, and release artifact checksums on tag push.
