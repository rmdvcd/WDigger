# Estado de los cambios de seguridad

Complementa la fotografía de [AUDITORIA_Y_PLAN.md](AUDITORIA_Y_PLAN.md). El plan sigue **parcialmente implementado** y no validado para producción.

## Acciones reales habilitadas de forma acotada

- WDigger exige `requireAdministrator` en cada inicio y valida el token elevado antes de crear la ventana. SFC, DISM, CHKDSK `/scan` (volumen del sistema u otra unidad seleccionada), vaciado de DNS y activación de Windows se ejecutan tras confirmación. Las herramientas de Windows se resuelven desde System32; no se busca un ejecutable por `PATH` ni junto al EXE.
- Windows Update intenta detener únicamente servicios inicialmente activos, conserva las cachés renombradas e intenta revertir renombres en caso de error y recuperar los servicios en `finally`. Un apagado o fallo del proceso puede interrumpir la recuperación: puede ser necesaria intervención manual y no se garantiza rollback.
- Se habilitan tres ajustes fijos de registro con copia del DWORD previo en HKLM y reversión condicionada a que el valor no haya cambiado externamente. El historial y JSON antiguos no permiten revertir; no se ejecutan comandos editables. Las claves vacías pueden permanecer.
- Se habilita la retirada de un Appx exacto elegido del usuario de la sesión elevada. No afecta a otros usuarios ni paquetes provisionados; no se garantiza recuperar datos o reinstalarlo.
- La interfaz exige elevación en el manifiesto (`requireAdministrator`); no hay auxiliar elevado. El rechazo de UAC impide el inicio. Esto simplifica el acceso a las operaciones revisadas, pero aumenta la superficie privilegiada de toda la interfaz.
- La política deniega por defecto el resto de acciones modificadoras en modo real, incluso con privilegios. Con simulación activa las acciones disponibles no ejecutan sus cambios; Acronis, WinFR y las consultas de Winget continúan suspendidas mientras no se pueda verificar de forma independiente su ruta, firma e identidad.
- La UI solicita confirmación explícita, exige un registro persistente, intenta crear/verificar un punto de restauración y solicita autorización por separado si no se verifica. Un punto de restauración no es rollback ni sustituye backups. Varias operaciones no ofrecen cancelación y no tienen timeout; un cierre forzado, fallo o apagado puede dejarlas a medias.

## Otras mejoras

- Diagnóstico parcial de lectura con categorías no implementadas marcadas como no disponibles e informe exportable de texto. No asegura que el sistema esté sano.
- Registro JSONL acotado y redacción básica; fallback a `%LOCALAPPDATA%`, y a memoria si no se puede escribir. La redacción no es exhaustiva, y crash logs e informes pueden contener datos personales. Salida por proceso limitada y lectura concurrente; se sigue drenando el pipe después de truncar.
- Se añadieron atajos, etiquetas de estado y recursos de alto contraste sin validación de Narrator/DPI en Windows. Pruebas automatizadas cubren política de autorización, diagnóstico, redacción, límites de salida, catálogo de ajustes, y reversión aislada en HKCU (solo Windows), además de ejecutor y paquetes. Las pruebas nuevas aún no se han ejecutado.

## Pendiente

1. Auxiliar elevado restringido y autenticado; eliminar la elevación de toda la UI.
2. Rollback real por tipo de operación y recuperación de estados parciales; rediseñar acciones suspendidas antes de habilitarlas.
3. Completar diagnóstico por categoría, interpretación localizada y el informe estructurado con cobertura real.
4. Revisar el resto de rutas de comandos, ejecutables de terceros, clasificación lectura/escritura y privacidad; validar accesibilidad y tratamiento de archivos compartidos.
5. Compilar y ejecutar las pruebas, smoke test y escenarios de restauración/UAC/simulación/errores/cierre en VMs desechables Windows 10/11. No se han compilado ni ejecutado las pruebas nuevas o reparaciones reales en esta sesión; no se han validado los scripts de Windows Update y Appx en Windows.

Las verificaciones estáticas no sustituyen la compilación ni las pruebas en Windows.
