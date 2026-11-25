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

### 🔴 P0 — Automatic 180° Orientation Calibration for Vertical Panels
* **Status:** Pending (Known issue in `v1.0.0`)
* **Problem:** Some vertical `360×960` chassis panels (e.g., Mars Gaming VMAX vertical units) have a 180° inverted physical scan direction compared to `DisplayDirection = 0`. Users currently select `180°` manually in the Screens tab (`270°` for `240×320` AIO).
* **Plan:**
  - Detect hardware panel orientation quirks by serial prefix / EDID / SOEYI `Isup` + `DisplayDirection` semantics.
  - Apply automatic upright orientation calibration on first attach while preserving manual user overrides.
  - Add pixel-coordinate SkiaSharp orientation regression tests (`0° / 90° / 180° / 270°`).

---

### 🟠 P1 — Native TURZX Theme & USB-Serial Hardware Compatibility
* **Status:** Planned — Full technical specification in [docs/TURZX_COMPATIBILITY_PLAN.md](docs/TURZX_COMPATIBILITY_PLAN.md)
* **Goal:** Native support for **TURZX** (Turing Smart Screen) `.smtheme` themes and direct USB-Serial panels without third-party conversion tools.
* **Phases:**
  1. **Phase 1 — Full `theme.yaml` v2/v3 Parser:** Support `DISPLAY_ORIENTATION`, `SCREEN_WIDTH/HEIGHT`, `STATIC_TEXT`, `DYNAMIC_TEXT`, `DYNAMIC_IMAGE`, `DYNAMIC_BAR`, `RADIAL_GAUGE`, `GRAPH`, and custom fonts.
  2. **Phase 2 — Smart Aspect-Ratio Scaler:** Scale `480×1920`, `720×1280`, `480×800`, and `320×480` TURZX layouts cleanly onto `360×960`, `240×320`, and `480×480` panels.
  3. **Phase 3 — SkiaSharp Gauge & Graph Widgets:** Implement radial arcs, needles, sparkline history graphs, and extended sensor keys (`CPU_FAN`, `GPU_CLOCK`, `RAM_USED_GB`, `DISK_TEMP`).
  4. **Phase 4 — `TurzxSerialBackend`:** Native `IDisplayBackend` over USB CDC (`System.IO.Ports`) for TURZX Rev A / B / C / E (`3.5"`, `5"`, `8.8"`).
  5. **Phase 5 — Golden-Frame Test Suite:** Automated pixel-verified regression tests per widget type.

---

### 🟠 P1 — Theme Maker Compatibility & Next-Gen Theme Creator (`SnakeMarsTheme`)
* **Status:** Active Ecosystem Integration
* **Goal:** Seamless 100% native compatibility with themes built in **Theme Maker** / [**SnakeMarsTheme**](https://github.com/Snakefoxu/marsmakertheme), plus a unified next-generation creator workflow:
  1. **Full Native Compatibility with Theme Maker Outputs:**
     - Parse all Theme Maker / `SnakeMarsTheme` `.photo`, `.smtheme`, `Setting.txt`, and Type 0/1 `.json` schemas (`Text`, `BorderLine`, `Image`, `gif`, `FontFamily`, `CornerRadius`, `Opacity`, `DisplayDirection`).
     - Guarantee sensor key parity across CPU, GPU, RAM, Disk, Network, Time/Date, Audio, and Weather tokens.
  2. **Live Editor ↔ Engine Bridge (`FoxuMarsDisplay_IPC_v1`):**
     - Instant 1-click *Apply & Preview* from Theme Maker directly onto live USB screens via Named Pipe IPC (`apply_theme`, `set_brightness`, `set_rotation`, `get_status`).
  3. **Next-Generation Integrated Theme Creator:**
     - WYSIWYG drag-and-drop canvas with snap guides, live telemetry binding, animated GIF/WebP timeline preview, multi-resolution presets (`240×320`, `320×240`, `360×960`, `480×480`, `320×1480`), and 1-click export to `.photo` + `.smtheme`.

---

### 🟡 P2 — Video Wallpapers, Per-Screen Slideshow UI & CI/CD
* **MP4 / WebM Hardware-Decoded Wallpapers:** Pre-cropped `.sdac` frame caching for video backgrounds.
* **Per-Screen Slideshow Controls:** UI controls for interval, theme playlist, and shuffle (backend `SlideshowEnabled` / `SlideshowIntervalSec` already supported in `config.json`).
* **GitHub Actions CI/CD:** Automated `.NET 8` build, unit test execution, and release artifact checksums on tag push.
