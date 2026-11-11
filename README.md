# WDigger

WDigger es una herramienta portable WinUI 3 de diagnóstico y reparación para Windows 10/11 x64. Está **en desarrollo y no está validada para producción**. Consulta el [plan vigente](PLAN_DE_CAMBIOS_2026-11-11.md), la [auditoría histórica](AUDITORIA_Y_PLAN.md) y el estado de seguridad en [ESTADO_CAMBIOS_SEGURIDAD.md](ESTADO_CAMBIOS_SEGURIDAD.md).

## Privilegios y seguridad operacional

WDigger declara `requireAdministrator` y verifica que el token del proceso esté elevado antes de crear la interfaz. Si se rechaza UAC, la aplicación no se abre. Esta es una decisión de producto actual: toda la UI, incluidos el diagnóstico y los selectores de archivos, se ejecuta elevada.

Las acciones que pueden cambiar Windows tienen un contrato explícito de riesgo, timeout, cancelación y recuperación. Antes de una operación real WDigger:

1. exige registro de auditoría y **journal de operación** persistentes;
2. muestra riesgo, recuperación disponible y política de cancelación, y exige confirmación final;
3. intenta crear y verificar un punto de restauración; si Windows no lo verifica, pide confirmación independiente para continuar;
4. registra el ciclo de vida, los pasos y el resultado tipado (`simulada`, `correcta`, `parcial`, `cancelada`, `reinicio`, `manual`, etc.); y
5. muestra las operaciones que quedaron en ejecución al iniciar de nuevo. No las reanuda automáticamente.

Un punto de restauración no es una copia de seguridad y no garantiza rollback. No apagues ni fuerces el cierre durante una operación. La cancelación puede dejar cambios parciales incluso cuando la UI permite solicitarla.

| Acción | Estado y limitación |
|---|---|
| SFC, DISM y CHKDSK `/scan` | Disponibles en sesión elevada, con journal, confirmación y timeout. Un código de proceso correcto no equivale por sí solo a un sistema sano. |
| Windows Update reset | Disponible como operación de riesgo alto. Conserva las cachés renombradas e intenta restaurar los servicios originalmente activos durante la misma ejecución; tras interrupción requiere revisión manual. |
| Ajustes de registro | Catálogo fijo de tres ajustes. Guarda un snapshot en HKLM y solo revierte tras verificar que el valor no cambió externamente. |
| Retirada Appx | Disponible para un único paquete removible del usuario elevado actual. No toca paquetes provisionados ni otros usuarios y no garantiza reinstalación o recuperación de datos. |
| DNS, red, dispositivos, controladores y limpieza temporal | Expuestos con validaciones progresivas; varias acciones permanecen solo en simulación hasta completar su revisión/validación por módulo. La limpieza previsualiza el alcance y no sigue puntos de redirección. |
| Winget, Chocolatey, Acronis y Windows File Recovery | Suspendidos, también como integración ejecutable, hasta verificar contrato, ruta, firma, versión, origen y destino en VM. |

Los scripts PowerShell habilitados son recursos internos versionados y revisados. Los módulos solo pueden solicitar un identificador de script y argumentos separados; no aceptan cuerpos de script, ejecutables de `PATH`, `cmd /c` ni líneas de comandos editables. Las herramientas nativas se resuelven desde `System32` mediante una allowlist cerrada.

## Diagnóstico e informes

El módulo **Diagnóstico** no modifica Windows y reúne resultados tipados, ordenados por severidad. Incluye versión de Windows, espacio y disponibilidad del volumen de sistema, adaptadores activos, DISM `CheckHealth`/`ScanHealth`, BCD, PnP con problemas, perfiles de firewall y eventos críticos recientes. Las consultas que no pueden ejecutarse se muestran de forma explícita como no aplicables, no disponibles, permiso denegado o error; nunca como sanas.

El panel de control muestra los hallazgos prioritarios del último análisis. SFC, DISM y CHKDSK señalan la comprobación de diagnóstico relacionada, pero siguen exigiendo revisión, confirmación y un técnico cuando la evidencia no sea concluyente.

La exportación crea un informe de soporte de texto con el diagnóstico y un resumen redactado de las operaciones recientes (IDs, riesgo, pasos, resultado y acciones manuales), sin salida completa de comandos. Revísalo antes de compartirlo.

## Datos, privacidad y simulación

- Los logs JSONL rotados se guardan junto al ejecutable o, si no se puede escribir allí, en `%LOCALAPPDATA%\WDigger\logs`.
- El journal se guarda bajo el directorio real de logs, en `operations`. Si el log o journal persistente no está disponible, WDigger bloquea los cambios reales.
- Los ajustes de UI usan una carpeta portable escribible o el fallback de `%LOCALAPPDATA%\WDigger\config`.
- Se eliminaron los JSON editables de ajustes y paquetes, junto con el historial legado de ajustes: no existe una configuración externa que pueda definir una acción elevada.
- Los logs y el informe aplican redacción básica de perfiles e IPv4; no es una garantía de anonimización. No se conserva la salida completa de comandos por defecto.
- El modo **Simulación** conserva las consultas de solo lectura y registra las acciones modificadoras sin ejecutarlas. Una simulación nunca se presenta como una reparación correcta.

Atajos: **F5** inicia el análisis; **Ctrl+R** abre la ayuda de reparaciones; **Ctrl+L** muestra u oculta la salida; **Esc** solicita cancelar cuando la política lo permite; **F1** abre ayuda.

## Compilar y publicar

Se necesita Windows 10/11 x64 y el SDK .NET 10:

```powershell
dotnet run --project tests/WDigger.SafetyTests/WDigger.SafetyTests.csproj -c Release
dotnet build src/WDigger/WDigger.csproj -c Release
dotnet publish src/WDigger/WDigger.csproj -c Release -p:PublishProfile=Portable-win-x64
```

El ejecutable se publica en `artifacts/portable/WDigger.exe`. El flujo `.github/workflows/portable-windows.yml` compila, ejecuta las pruebas de seguridad y publica el artefacto portable.

## Validación pendiente obligatoria

Esta rama se desarrolló sin SDK .NET ni Windows disponible localmente. La compilación, pruebas de seguridad y publicación deben confirmarse mediante CI Windows. Las pruebas funcionales de UAC, WMI, System Restore, DISM/SFC/CHKDSK, Windows Update reset, Appx, medios de solo lectura, cancelación/cierre, alto contraste, 100–200 % DPI y Narrator requieren VMs Windows desechables con snapshot. No ejecutes reparaciones reales en CI ni en equipos de producción para validar esta versión.
