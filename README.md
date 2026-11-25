# FoxuMarsDisplay

[![Ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/oscarlosu)

Ultra-lightweight native SkiaSharp display controller for Mars Gaming VMAX, SOEYI (MacroSilicon MS912C/MS9132 & AIC), and TURZX themes on Windows.

## Features
- **~10x lower CPU & ~5x lower RAM** than stock SOEYI software (zero-allocation BGRA8888 rendering + `.sdac` pre-cropped WebP animation cache).
- **2 Hz USB Keep-Alive** prevents MS9132 hardware OSD watchdog resets while keeping static themes at 0% render CPU.
- **Unified Theme Support**: SOEYI `Setting.txt` & Type 0 JSON, `.photo` archives (native extraction, no 7-Zip required), TURZX `.smtheme` / `theme.yaml`, PNG sequences, GIFs, and static images.
- **Hardware Sensors**: NVIDIA NVML + LibreHardwareMonitorLib (PawnIO) + Network throughput.
- **Full Control**: Multi-screen hot-plug, brightness (0–100%), rotation (0°/90°/180°/270°), screen on/off, slideshow, Game Mode auto-pause, System Tray (`--tray`), and Named Pipe IPC for [SnakeMarsTheme](https://github.com/Snakefoxu/marsmakertheme).

## Build & Test
```powershell
dotnet build FoxuMarsDisplay.sln -c Release -p:Platform=x64
dotnet test tests/FoxuMarsDisplay.Core.Tests/FoxuMarsDisplay.Core.Tests.csproj -c Release -p:Platform=x64
```
