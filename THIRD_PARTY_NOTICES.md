# Third-Party Notices & Attributions

FoxuMarsDisplay is proprietary source-available software (`Copyright (c) 2026 oscarlosu-lab`, see [LICENSE](LICENSE)).

## NuGet & Runtime Dependencies
- **SkiaSharp** — MIT License (https://github.com/mono/SkiaSharp)
- **SharpCompress** — MIT License (https://github.com/adamhathcock/sharpcompress)
- **LibreHardwareMonitorLib** — Mozilla Public License 2.0 (https://github.com/LibreHardwareMonitor/LibreHardwareMonitor)

## Clean-Room Interoperability & MIT / Apache-2.0 Ecosystem References
All USB protocol implementations in `FoxuMarsDisplay.Core` were written cleanly in C# without incorporating GPL or proprietary third-party code. The following permissively licensed community projects served as technical references:

- `2967465057/Soeyi-Control-WinUI3-Rewrite` (MIT) — Native DLL export signatures and USB PnP recovery patterns
- `dorimeromod-lang/VmaxThemeStudio` (MIT) — `Setting.txt` widget serialization patterns and WCAG auto-contrast concept
- `itsmylife44/LcdFusion` (MIT) — USB PnP error detection patterns
- `usausa/turing-smart-screen` (MIT) & `xushouzun/TurzxOpenSource` (MIT) — TURZX Win207 VID:PID catalog & `.NET` display helpers
- `ShellWen/dida-box` (MIT) — SOEYI USB-CDC DLS handshake reference (`F0 A5 5A 0F`)
- `hogancv/valkyrie-240-lcd` (MIT) & `lucaslopezag-eng/open-software-for-ML-VISIONPRO240` (MIT) — MacroSilicon MS9132 HID/Bulk protocol reference
- `OJPalenzuela/lyricvision-lcd` (Apache-2.0) — Synced LRC lyrics layout concepts
- `Yevgeniy-Olexandrenko/tssp-themes` (MIT) & `jdcortes-dev/turing-smart-screen-python-HELLDIVERS-Landscape-theme` (MIT) — Community `theme.yaml` themes
