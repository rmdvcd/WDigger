# Validación en VM Windows

Esta guía convierte las validaciones pendientes de WDigger en un protocolo reproducible. **No ejecutes operaciones reales en equipos de producción.** Usa una VM desechable, toma un snapshot antes de cada bloque y conserva el journal e informe generado como evidencia.

## Preparación

1. Crea VMs limpias de Windows 10 y Windows 11 x64 con las versiones realmente soportadas.
2. Toma un snapshot inicial y registra edición, build, idioma, zona horaria, si System Restore está activo y el estado de Windows Update.
3. Copia el EXE portable publicado o compila la rama. Ejecuta las pruebas de seguridad primero:

   ```powershell
   dotnet run --project tests\WDigger.SafetyTests\WDigger.SafetyTests.csproj -c Release
   dotnet build src\WDigger\WDigger.csproj -c Release
   ```

4. Para la comprobación de arranque no destructiva, abre PowerShell como administrador y ejecuta:

   ```powershell
   .\ci-check-app-start.ps1
   ```

   El script confirma el arranque elevado y la ausencia de un crash log nuevo; no pulsa acciones de reparación.

## Matriz de ejecución

| ID | Escenario | Pasos | Evidencia / criterio de aceptación |
|---|---|---|---|
| VM-01 | UAC aceptado | Inicia `WDigger.exe`, acepta UAC y ejecuta Diagnóstico. | La ventana abre, muestra ADMINISTRADOR y el análisis termina sin crash. |
| VM-02 | UAC rechazado | Inicia el EXE y rechaza UAC. | No se muestra la UI ni se inicia una operación. Documenta el código/diálogo de Windows. |
| VM-03 | Journal interrumpido | Inicia una operación de bajo riesgo en simulación; para una operación real aprobada, no la interrumpas salvo en snapshot. Cierra forzadamente solo en la VM de snapshot. | Al reiniciar, WDigger lista la operación `Running`, no la reanuda y muestra la carpeta de journal. |
| VM-04 | System Restore y WMI activo/inactivo | Antes de cada ejecución, registra `Get-CimInstance Win32_OperatingSystem` y el estado de System Restore. Prueba una acción real de bajo riesgo con Restore activo; repite con Restore desactivado y rechaza/acepta explícitamente continuar. En un snapshot separado, simula/inhabilita temporalmente el acceso WMI si la política de la VM lo permite. | El journal registra creación verificada o fallo; no se cambian acciones tras rechazar continuar sin punto; un fallo WMI se declara claramente y no se convierte en falso éxito. |
| VM-05 | Medio de solo lectura | Ejecuta desde un USB/VHD montado de solo lectura. | Logs/journal usan `%LOCALAPPDATA%\WDigger\logs`; la UI muestra la ubicación. Si no se puede persistir, bloquea cambios reales. |
| VM-06 | Cancelación y timeout | Usa una operación cancelable de lectura, y en snapshots prueba una operación que exceda su timeout o cancela un paso no destructivo. | Resultado `Cancelled`/`Failed` o `Partial`; journal incluye el paso; nunca se comunica éxito genérico. |
| VM-07 | Windows Update reset | Solo en snapshot, con y sin red. Ejecuta y, por separado, fuerza un fallo controlado de servicio/carpeta. | Servicios originalmente activos quedan iniciados o el journal da instrucciones manuales; no se borran copias `.WDigger-*`. |
| VM-08 | Appx | En snapshot, selecciona un paquete removible de prueba del usuario actual y repite tras invalidar la selección. | Solo se afecta el paquete exacto seleccionado; paquete no apto/caducado se bloquea; resultado declara recuperación manual. |
| VM-09 | SFC/DISM/CHKDSK | Ejecuta únicamente en snapshots, en Windows en español e inglés. | Timeout y resultado se registran; la UI no afirma salud solo por texto localizado; se indica reinicio/manual cuando aplique. |
| VM-10 | DPI, alto contraste y Narrator | Repite navegación y Diagnóstico a 100 %, 125 %, 150 % y 200 %, con texto ampliado, temas y alto contraste. Navega solo con teclado y Narrator. | No hay truncamiento crítico; tarjetas y estado tienen nombre/ayuda; cambios de resultado se anuncian una vez. |
| VM-11 | Privacidad e informe | Exporta un informe y revisa JSONL/journal. | El informe correlaciona IDs y estados, no conserva salida completa, y redacta rutas de perfil/IP evidentes. |

## Registro de evidencia

Para cada fila, guarda: fecha UTC, identificador de VM/snapshot, versión de WDigger, versión/build/idioma de Windows, acción exacta, estado final, journal JSON, informe exportado y cualquier captura relevante. No compartas logs sin revisarlos: la redacción es deliberadamente limitada.

## Límites

El CI solo debe compilar, ejecutar pruebas seguras, validar recursos y publicar el EXE. No debe ejecutar SFC, DISM, CHKDSK, reset de Windows Update, cambios de red, Appx, drivers, registro ni limpieza real. VM-01 a VM-11 requieren revisión humana y no se consideran aprobados por el pipeline.
