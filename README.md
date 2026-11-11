# WDigger

WDigger es una herramienta portable WinUI 3 de diagnóstico y reparación de Windows 10/11 x64. Esta versión está **en desarrollo y no está validada para producción**. Consulta la auditoría histórica [AUDITORIA_Y_PLAN.md](AUDITORIA_Y_PLAN.md), el plan vigente [PLAN_DE_CAMBIOS_2026-11-11.md](PLAN_DE_CAMBIOS_2026-11-11.md) y [ESTADO_CAMBIOS_SEGURIDAD.md](ESTADO_CAMBIOS_SEGURIDAD.md).

## Permisos y operaciones reales

WDigger declara `requireAdministrator` en el manifiesto y **solo se inicia con un token elevado de administrador**. Windows muestra el aviso de Control de cuentas de usuario en cada inicio; si se rechaza, la aplicación no llega a abrirse. Como no existe un auxiliar aislado, se eleva toda la interfaz, incluidos diagnóstico y navegación. Las operaciones de terceros y las configuraciones editables siguen sin estar autorizadas para cambios reales.

| Operación | Estado actual |
|---|---|
| SFC `/scannow` | Disponible en sesión elevada, tras confirmación. |
| DISM `/Online /Cleanup-Image /RestoreHealth` | Disponible en sesión elevada, tras confirmación. Puede utilizar Windows Update. |
| CHKDSK `/scan` en el volumen del sistema u otra unidad seleccionada | Disponible en sesión elevada, tras confirmación. Windows puede hacer correcciones o solicitar un reinicio. |
| Windows Update | Renombra las dos cachés, conserva las carpetas anteriores e intenta restaurar los servicios activos originalmente, también ante errores. No garantiza recuperación tras cierre forzado. |
| Ajustes de registro | Tres ajustes fijos: telemetría, Cortana e inicio rápido. Se registra el DWORD previo y se permite revertirlo si no ha cambiado externamente. No se ejecutan comandos del JSON editable. |
| Retirar Appx | Selección de un paquete exacto del usuario de la sesión elevada; no afecta paquetes provisionados ni otros usuarios. No se garantiza reinstalación ni recuperación de datos. |
| Vaciar caché DNS y activar Windows | Disponibles en sesión elevada. La activación utiliza la licencia ya configurada. |
| Otras acciones modificadoras | Solo simulación; no disponibles en modo real. |
| Restauración Acronis y WinFR | Suspendidas, también en simulación, hasta poder verificar herramientas y destinos físicos. |

Las acciones reales habilitadas usan código y herramientas de Windows, no comandos de configuración editable; requieren registro persistente y un intento de creación y verificación de punto de restauración. Si este falla, se pide autorización explícita para continuar sin él. **Un punto de restauración no garantiza rollback y no sustituye una copia de seguridad.** Guarda el trabajo, realiza una copia y mantén alimentación estable antes de ejecutar. Algunas operaciones no ofrecen cancelación en la UI; un apagado o cierre forzado puede dejar cambios parciales. El resultado del comando no garantiza por sí solo que Windows esté sano.

### Recuperación de ajustes y Windows Update

Los ajustes de registro están limitados a un catálogo fijo y guardan una copia en `HKLM\SOFTWARE\WDigger\RegistryUndo` antes de escribir. La reversión comprueba que el valor siga siendo el aplicado. Una copia incompleta, un cambio externo o una interrupción pueden requerir revisión manual; las claves vacías creadas durante la aplicación no se eliminan. Los ajustes heredados no tienen copia previa. Las directivas de una organización pueden sustituir los cambios.

Windows Update conserva las carpetas renombradas con sufijo `.WDigger-<id>`. Si hay error, intenta devolver los nombres originales y arrancar los servicios que estaban activos. Si una interrupción impide completar la recuperación o se informa de error, revisa manualmente los servicios y las carpetas originales y de copia antes de modificar nada; **no se ofrece rollback transaccional ni recuperación automática tras un apagado**. No borres las copias hasta verificar el funcionamiento.

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

**Verificación pendiente:** compilar y ejecutar pruebas y escenarios en máquinas virtuales desechables Windows 10/11; probar la aceptación y el rechazo de UAC (el rechazo debe impedir el inicio), restauración, cancelación, salida y rutas, y alto contraste/Narrator. No se han probado reparaciones reales en esta sesión. El plan completo, especialmente un auxiliar elevado con mínimo privilegio, rollback e interpretación localizada, continúa pendiente.
