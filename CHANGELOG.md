# Changelog

## [v2.1.0] — 2026-11-25 — Clean-Room Engine & Hardware Expansion
- **Serial Tile Differential Engine (`SerialTileDiffEngine` + Win32 Handle):** Replaced `SerialPort` with direct Win32 serial handles and a 16×16 tile dirty-rectangle differ with byte-budget pacing for high-FPS updates over bandwidth-limited USB serial displays.
- **Native WinUSB SLV3 Backend (`TurzxWinUsbBackend`):** Direct SetupAPI + WinUSB transport with DES-CBC 512-byte command framing (`VID_1CBE`) and constrained JPEG encoding, plus expanded `UsbDisplayCatalog` device registry (WeAct, Thermaltake HID, NZXT Kraken, Corsair Elite, Gigabyte AORUS).
- **Enhanced Media, Lyrics & Ambient Art:** Added `LyricsOffsetMs` timing slider (`-5000..+5000 ms`), browser/video title cleanup (`CleanTrackQuery`), automatic Deezer album-art fallback, and radial dominant-color ambient backgrounds in `SkiaThemeRenderer`.
- **Automatic Day/Night Brightness (`BrightnessSchedule`):** Configurable overnight or daytime brightness schedule evaluated automatically without overwriting user daytime presets.
- **Studio Proportional Canvas Scaler (`ThemeCanvasScaler`):** Automatic proportional widget scaling when switching target resolutions or screens in `FOXU // STUDIO`.
- **Local Web/REST Remote Control (`LocalHttpRemoteServer`):** Loopback HTTP server (`127.0.0.1`) with optional Bearer token authentication and built-in dark Web UI.

## [v2.0.0] — 2026-11-25 — Unicorn Engine
- **Native MP4 Video Backgrounds:** Zero-dependency Windows Media Foundation COM decoder (`MediaFoundationVideoDecoder`) converting `back.mp4` / `video.mp4` into hardware-ready `.sdac` WebP frame caches.
- **Full SOEYI `Setting.txt` & TURZX `theme.yaml` Parity:** Added `RingProgressBar`, decorative & bordered `BorderLine`, `GridLine` (`maxcount`, `Margin`, `Orientation`), `Text` `Title@` prefix + `TextAlignment` + `IsShowWidth`, `IsEnableItem` status icons, `AudioVisualizerControl`, `SonglyricsControl`, `SongIcon`, `foxu-theme-v1` JSON themes, and fixed `MEMORY.VIRTUAL` / `GPU.MEMORY` mappings.
- **Multi-Hardware USB Ecosystem:** Added `UsbDisplayCatalog` covering 6 hardware families (MacroSilicon MS9132/MS912C, HL-VMAX/SOEYI CDC, ArtInChip, TURZX Win207/Serial, Thermalright LY/HID/ChiZhu, MiraBox), live `SerialCdcVmaxBackend` & `TurzxSerialBackend`, and clean-room `MultiProtocolEncoders`.
- **Hardware Control & Recovery:** Real backlight dimming via `MSDISPLAYSDKWRRAPER.dll` (`MSDisplaySetBrightness`) with automatic software fallback, firmware query, and `UsbRecoveryHelper` (`pnputil`).
- **WASAPI Loopback FFT, Media & Telemetry:** 16-band Cooley-Tukey FFT spectrum analyzer, LRCLIB synced lyrics + iTunes album art with vinyl spin mode, Open-Meteo weather + ICMP ping, and optional AI quota sensors (`AI_CLAUDE`, `AI_CODEX`, `AI_GEMINI`).
- **Automation & Studio:** Foreground game auto-profile switching, idle screensaver switching, integrated WPF WYSIWYG Theme Editor (`ThemeEditorWindow`) with WCAG auto-contrast and `.smtheme` / `Setting.txt` exporter, GitHub Releases `UpdateChecker`, and CI workflow.

## [v1.1.0] — Universal Screen UX & Native TURZX YAML Engine
- Screen-first Gallery/Store UX, universal same-orientation + near-square AIO compatibility, `ThemeScaleMode`, `RemoteZipPreviewExtractor`, and embedded verified HF resolutions.

## [v1.0.0] — Initial Cyber-HUD Release
- Ultra-lightweight SkiaSharp renderer, SOEYI `.photo`/`.smtheme` loader, zero-UAC installer, and 5-language localization.
