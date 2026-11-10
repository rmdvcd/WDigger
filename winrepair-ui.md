---
name: winrepair-ui
description: >-
  Guía de UI para una herramienta nativa de diagnóstico y reparación de PC en
  Windows (WinUI 3 / WPF / .NET). Úsala al crear o revisar pantallas de
  diagnóstico, listas de problemas, acciones de reparación, barras de progreso
  y diálogos de elevación. Cubre Fluent Design, alto contraste, DPI, tamaños
  de toque y presentación de resultados técnicos sin abrumar.
---

# UI de la Herramienta de Reparación Windows: Diagnóstico, Progreso y Acciones

Esta skill define cómo debe verse y componerse la interfaz de una herramienta nativa de diagnóstico y reparación para Windows. El usuario llega con un problema, no con ganas de explorar: la UI debe **diagnosticar rápido, explicar claro y reparar con seguridad**.

## 1. Estructura de la ventana principal

La estructura canónica de una herramienta de reparación es:

| Zona | Contenido |
|---|---|
| **Barra superior** | Nombre de la herramienta, estado de elevación (escudo UAC), botón de ajustes |
| **Panel izquierdo** | Categorías de diagnóstico: Sistema, Red, Disco, Arranque, Drivers, Seguridad, Rendimiento |
| **Panel central** | Lista de comprobaciones de la categoría seleccionada, con estado (OK / Advertencia / Error / No aplica) |
| **Panel inferior** | Botón “Analizar todo”, botón “Reparar seleccionados”, barra de progreso global |

Usa **WinUI 3 con NavigationView** (o WPF con un layout equivalente) para que la herramienta se sienta nativa y respete el tema del sistema (claro/oscuro) y el acento del usuario.

## 2. Presentación de resultados de diagnóstico

- Cada comprobación tiene **tres niveles de severidad**: `OK` (verde), `Advertencia` (ámbar), `Error` (rojo). No uses solo color: añade icono y texto.
- El resultado técnico crudo (código de salida de `sfc`, evento del log, valor del registro) va en un **panel expandible** “Detalles técnicos”, no en la cara principal.
- La cara principal muestra una **explicación en lenguaje llano**: “El archivo de sistema `x` está dañado. Esto puede causar cierres inesperados de aplicaciones.”
- Ordena los problemas por **severidad y por impacto**, no por orden de comprobación. Un error de disco que impide arrancar va antes que una advertencia de fuente.

## 3. Acciones de reparación

- Cada problema tiene una **acción recomendada** y, cuando aplica, **acciones alternativas** en un menú desplegable. La acción principal va en un botón primario; las alternativas, en un botón secundario.
- Las acciones destructivas (borrar archivos, modificar el registro, desinstalar drivers) se marcan visualmente con un icono de advertencia y requieren confirmación explícita (ver `winrepair-ux`).
- El botón “Reparar seleccionados” muestra **cuántas acciones** se van a ejecutar y **cuáles son destructivas** antes de continuar.

## 4. Progreso y estado

- Las operaciones largas (SFC, DISM, CHKDSK) muestran **progreso determinado** cuando el comando lo permite (`DISM /Online /Cleanup-Image /RestoreHealth` reporta porcentaje) e **indeterminado** cuando no.
- La barra de progreso global muestra el avance del conjunto; cada comprobación tiene su propio indicador.
- El usuario puede **cancelar** una operación en curso (excepto las que no admiten cancelación segura, como una reparación de disco en curso), y la UI debe indicar si la cancelación es segura.
- **Log en vivo** en un panel inferior colapsable: cada comando ejecutado, su salida y su código de retorno. Esto es esencial para soporte y auditoría.

## 5. DPI, alto contraste y tamaños

- Soporta **DPI 100%–200%** sin truncamientos. Usa unidades lógicas (DIPs) y evita tamaños fijos en píxeles.
- Respeta el **modo de alto contraste** de Windows: no uses colores hardcodeados; usa los recursos del tema del sistema (`SystemColors`, `ThemeResource`).
- Botones y controles interactivos con área mínima de **32×32 DIP** (Windows es más permisivo que iOS, pero 44×44 sigue siendo la recomendación táctil).
- Fuentes: Segoe UI Variable en WinUI 3; respeta el escalado de texto del sistema.

## 6. Checklist de UI

- [ ] ¿La ventana usa NavigationView (WinUI 3) o equivalente con tema claro/oscuro del sistema?
- [ ] ¿Cada resultado de diagnóstico tiene icono + texto además de color?
- [ ] ¿Los detalles técnicos están en un panel expandible, no en la cara principal?
- [ ] ¿La acción recomendada es un botón primario y las alternativas están en un menú?
- [ ] ¿Las acciones destructivas se marcan visualmente antes de ejecutarse?
- [ ] ¿Las operaciones largas muestran progreso determinado o indeterminado según el comando?
- [ ] ¿La UI soporta DPI 200% y alto contraste sin truncamientos?