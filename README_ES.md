# FoxuMarsDisplay

[![Ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/oscarlosu)

Controlador nativo ultra-ligero con SkiaSharp para pantallas LCD USB Mars Gaming VMAX, SOEYI (MacroSilicon MS912C/MS9132 y AIC) y temas TURZX en Windows.

## Características
- **~10× menos CPU y ~5× menos RAM** que el software oficial de SOEYI (renderizado BGRA8888 sin asignaciones + caché `.sdac` WebP pre-recortada).
- **Keep-Alive USB a 2 Hz**: evita reinicios del watchdog OSD del chip MS9132 manteniendo el renderizado estático al 0% de CPU.
- **Soporte Unificado**: carpetas SOEYI (`Setting.txt` y JSON Tipo 0), paquetes `.photo` (sin necesidad de 7-Zip), TURZX `.smtheme` / `theme.yaml`, secuencias PNG, GIFs e imágenes.
- **Sensores Hardware**: NVML directo para NVIDIA + LibreHardwareMonitorLib (PawnIO) + Red.
- **Control Completo**: Multi-pantalla, brillo 0–100%, rotación 0°/90°/180°/270°, encendido/apagado, carrusel, Modo Juego, bandeja del sistema (`--tray`) e IPC por Named Pipe para SnakeMarsTheme.

## Compilación y Tests
```powershell
dotnet build FoxuMarsDisplay.sln -c Release -p:Platform=x64
dotnet test tests/FoxuMarsDisplay.Core.Tests/FoxuMarsDisplay.Core.Tests.csproj -c Release -p:Platform=x64
```
