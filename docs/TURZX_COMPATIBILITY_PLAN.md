# Plan de Compatibilidad Completa con Temas TURZX (`.smtheme` / `theme.yaml` / Hardware Serial)

Este documento define la hoja de ruta técnica para llevar la compatibilidad de **FoxuMarsDisplay** con el ecosistema **TURZX** (3.5", 5", 8.8", 2.1" circular y paquetes `.smtheme`) al 100%, manteniendo un consumo `< 1%` CPU y `< 60 MB` RAM.

---

## Estado Actual en FoxuMarsDisplay
* **Soportado hoy**:
  * Carga directa de archivos `.smtheme` (ZIP) y carpetas con `manifest.json` + `theme.yaml` básico (`DISPLAY_WIDTH`, `DISPLAY_HEIGHT`, `STATIC_IMAGES`, `STATS` simples).
  * Escaneo unificado en galería (`ThemeLibraryScanner`), extracción de `preview.png`, detección de resolución desde `manifest.json` o nombre del archivo (`7201280`, `480480`, `0.96Inch`, etc.) y descarga desde el catálogo HuggingFace.
* **Limitaciones actuales**:
  * Muchos temas TURZX oficiales usan jerarquías YAML anidadas (`STATS.CPU.TEMPERATURE.TEXT`, `RADIAL`, `BAR`, `LINE_GRAPH`, `STATIC_TEXT`) que aún no se parsean por completo.
  * Los temas TURZX están diseñados para resoluciones TURZX (`320×480`, `480×800`, `480×1920`, `720×1280`, `480×480`) y necesitan escalado inteligente cuando se aplican en pantallas Mars Gaming / SOEYI (`240×320`, `360×960`).
  * No existe todavía backend USB-CDC serie para pantallas físicas TURZX sin chip MacroSilicon/AIC.

---

## Hoja de Ruta en 5 Fases

### Fase 1 — Parser completo `theme.yaml` v2/v3 (`UnifiedThemeLoader`)
* **Objetivo**: Soportar la estructura jerárquica completa de `turing-smart-screen-python` y el software oficial TURZX Windows.
* **Cambios**:
  * Añadir parser YAML estructurado (máquina de estados por indentación o `YamlDotNet` ligero) para leer:
    * `display`: `DISPLAY_WIDTH`, `DISPLAY_HEIGHT`, `DISPLAY_ORIENTATION`, `DISPLAY_RGB_LED`.
    * `static_images` y `background_image` (capas Z-order múltiples + GIF/WebP animados).
    * `static_text`: etiquetas fijas, fuentes `.ttf`/`.otf` embebidas en `fonts/`, alineación (`LEFT`/`CENTER`/`RIGHT`) y `ALPHA`.
    * `STATS.*`: `TEXT` (`SHOW`, `X`, `Y`, `FONT`, `FONT_SIZE`, `FONT_COLOR` en `R,G,B` o `#HEX`), `GRAPH` (`BAR`, `RADIAL`, `LINE_GRAPH`).
* **Criterio de éxito**: `UnifiedThemeLoader.Load` carga sin pérdida el 100% de widgets de un `theme.yaml` complejo de TURZX.

### Fase 2 — Escalado Inteligente Aspect-Preserving (`SkiaThemeRenderer`)
* **Objetivo**: Permitir que un tema TURZX `480×1920` o `720×1280` se vea nítido y centrado en pantallas `360×960` o `240×320` sin recortar widgets.
* **Cambios**:
  * `SkiaThemeRenderer` ya dibuja sobre un lienzo lógico (`CanvasWidth × CanvasHeight`) y blitea al panel físico. Se añadirá modo de ajuste (`Fit` / `Fill` / `Stretch`) configurable por pantalla en `ScreenProfile` y ajuste automático de `Letterbox` oscuro `#000000` cuando la relación de aspecto difiera.

### Fase 3 — Widgets Avanzados y Sensores Extra (`SkiaThemeRenderer` + `LightweightSensorProvider`)
* **Objetivo**: Paridad visual con temas TURZX ricos en gráficos.
* **Widgets nuevos**:
  * **Arcos / Medidores Radiales (`RadialWidgetSpec`)**: `SKCanvas.DrawArc` con ángulo inicial, barrido dinámico (`0..360°`), grosor, puntas redondeadas (`SKStrokeCap.Round`) y color de fondo/relleno.
  * **Gráficas de Historial (`GraphWidgetSpec`)**: buffer circular de las últimas 60 muestras por sensor (`CpuUsage`, `GpuUsage`, `NetDown`) renderizado con `SKPath` + relleno degradado semitransparente.
  * **Agujas rotatorias / Iconos dinámicos**: rotación `SKCanvas.RotateDegrees` sobre sprites PNG según porcentaje.
* **Sensores nuevos**:
  * Uso/temperatura de discos (`DriveInfo` + NVMe/SMART vía LHM), carga VRAM GPU (`GpuMemoryLoad`), fecha formateada localizada y estado multimedia Windows (`SMTC`).

### Fase 4 — Backend Nativo para Pantallas Físicas TURZX (`TurzxSerialBackend`)
* **Objetivo**: Controlar directamente pantallas TURZX conectadas por USB-CDC (`COMx`) además de las pantallas MacroSilicon (`MS9132`) y `AIC`.
* **Cambios**:
  * Implementar `TurzxSerialBackend : IDisplayBackend` en `FoxuMarsDisplay.Core/Backends/` usando `System.IO.Ports.SerialPort` con detección PnP (`VID_1A86` / `VID_10C4` / `VID_0483` + handshake Revision A/B/C/E).
  * Conversión rápida `BGRA8888 → RGB565` en búfer preasignado (0 asignaciones por frame) y envío por bloques sucios (*dirty rects*) para mantener el puerto serie fluido.
  * Integrarlo en `CompositeDisplayBackend` para que conviva sin conflictos con MacroSilicon y AIC.

### Fase 5 — Matriz de Pruebas Golden-Frame y Presupuesto de Rendimiento
* **Objetivo**: Garantizar estabilidad y bajo consumo tras ampliar el soporte TURZX.
* **Verificaciones**:
  * Tests unitarios en `FoxuMarsDisplay.Core.Tests` renderizando `.smtheme` reales de `320×480`, `480×480`, `720×1280` y `480×1920` con aserciones de píxeles no vacíos y caché `.sdac`.
  * Benchmark de 20 s asegurando `< 1%` CPU total y `< 60 MB` RAM privada.
