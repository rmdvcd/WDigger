# WDigger

WDigger es una herramienta portable WinUI 3 de diagnóstico y reparación de Windows 10/11 x64. Esta versión está **en desarrollo y no está validada para producción**. Consulta [AUDITORIA_Y_PLAN.md](AUDITORIA_Y_PLAN.md) y [ESTADO_CAMBIOS_SEGURIDAD.md](ESTADO_CAMBIOS_SEGURIDAD.md).

## Permisos y operaciones reales

La interfaz arranca con los permisos del usuario (`asInvoker`). El diagnóstico no necesita elevación. Para utilizar las tres operaciones reales habilitadas, abre explícitamente WDigger **como administrador**. Esto eleva toda la aplicación: todavía no existe un auxiliar aislado. Las operaciones de terceros y configuraciones editables no están autorizadas para cambios reales.

| Operación | Estado actual |
|---|---|
| SFC `/scannow` | Disponible en sesión elevada, tras confirmación. |
| DISM `/Online /Cleanup-Image /RestoreHealth` | Disponible en sesión elevada, tras confirmación. Puede utilizar Windows Update. |
| CHKDSK `/scan` en el volumen del sistema | Disponible en sesión elevada, tras confirmación. Windows puede hacer correcciones o solicitar un reinicio. |
| Otras acciones modificadoras | Solo simulación; no disponibles en modo real. |
| Ajustes JSON, reparación de Windows Update, restauración Acronis, WinFR, eliminación Appx | Suspendidos, también en simulación, hasta su rediseño. |

Las acciones reales habilitadas usan ejecutables de Windows resueltos desde System32 y argumentos fijos; requieren registro persistente y un intento de creación y verificación de punto de restauración. Si este falla, se pide autorización explícita para continuar sin él. **Un punto de restauración no garantiza rollback y no sustituye una copia de seguridad.** Guarda el trabajo, realiza una copia y mantén alimentación estable antes de ejecutar. Estas operaciones no ofrecen cancelación en la UI: un apagado o cierre forzado puede dejar cambios parciales. El resultado del comando no garantiza por sí solo que Windows esté sano.

## Diagnóstico y privacidad

El módulo Diagnóstico reúne comprobaciones de solo lectura y permite exportar un informe de texto. Actualmente consulta espacio del disco del sistema, adaptadores activos y versión del SO; el resto de categorías se marca como **no disponible**. Los resultados no sustituyen un diagnóstico técnico. La exportación escribe el archivo seleccionado incluso en simulación.

El interruptor de simulación evita ejecutar las acciones modificadoras; las consultas de lectura siguen ejecutándose. WDigger puede escribir sus propios ajustes y registros. La simulación no es un éxito real.

Los registros se guardan en JSONL junto al ejecutable o, si no es posible, en `%LOCALAPPDATA%\WDigger\logs`. El registro rota a 2 MiB por archivo y conserva hasta 10 archivos de mejor esfuerzo. Si ambos destinos fallan se conserva solo información temporal en memoria y se bloquean los cambios reales. No se guarda la salida completa de los comandos; hay redacción básica, no exhaustiva. Los informes, salida visible y `WDigger.crash.log` pueden contener datos sensibles: revísalos antes de compartirlos. Si el medio portable es de solo lectura se usan las configuraciones incrustadas, con ajustes no persistentes si no pueden guardarse.

Atajos: F5 analizar, Ctrl+R información de reparaciones, Ctrl+L mostrar/ocultar salida, Esc solicitar cancelación cuando esté permitida, F1 ayuda. Narrator, contraste y DPI aún requieren pruebas en Windows.

## Compilar y publicar

Necesitas Windows 10/11 x64 y el SDK .NET 10. Desde la raíz:

```powershell
dotnet run --project tests/WDigger.SafetyTests/WDigger.SafetyTests.csproj -c Release
dotnet build src/WDigger/WDigger.csproj -c Release
dotnet publish src/WDigger/WDigger.csproj -c Release -p:PublishProfile=Portable-win-x64
```

El ejecutable se genera en `artifacts/portable/WDigger.exe`. `.github/workflows/portable-windows.yml` prepara las pruebas, compilación y publicación; `ci-check-app-start.ps1` es una prueba de arranque opcional para Windows interactivo.

**Verificación pendiente:** compilar y ejecutar pruebas y escenarios en máquinas virtuales desechables Windows 10/11; probar UAC, denegación de permisos, restauración, cancelación, salida y rutas, y alto contraste/Narrator. No se han probado reparaciones reales en esta sesión. El plan completo, especialmente auxiliar elevado, rollback e interpretación localizada, continúa pendiente.
