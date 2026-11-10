# Estado de los cambios de seguridad

Complementa la fotografía de [AUDITORIA_Y_PLAN.md](AUDITORIA_Y_PLAN.md). El plan sigue **parcialmente implementado** y no validado para producción.

## Acciones reales habilitadas de forma acotada

- SFC `/scannow`, DISM `/Online /Cleanup-Image /RestoreHealth` y CHKDSK `/scan` del volumen del sistema pueden ejecutarse en una sesión abierta explícitamente como administrador. Utilizan rutas de System32 y argumentos fijos.
- La interfaz arranca como usuario (`asInvoker`); no hay auxiliar elevado. Si se requiere ejecutar las tres acciones hay que reiniciar toda la aplicación manualmente como administrador, lo que aumenta la superficie privilegiada.
- La política deniega por defecto el resto de acciones modificadoras en modo real, incluso con privilegios. Con simulación activa las acciones disponibles no ejecutan sus cambios; ajustes JSON, Windows Update, Acronis, WinFR y eliminación Appx están totalmente suspendidos.
- La UI solicita confirmación explícita, exige un registro persistente, intenta crear/verificar un punto de restauración y solicita autorización por separado si no se verifica. Un punto de restauración no es rollback ni sustituye backups. Las tres operaciones no ofrecen cancelación y no tienen timeout; un cierre forzado, fallo o apagado puede dejarlas a medias.

## Otras mejoras

- Diagnóstico parcial de lectura con categorías no implementadas marcadas como no disponibles e informe exportable de texto. No asegura que el sistema esté sano.
- Registro JSONL acotado y redacción básica; fallback a `%LOCALAPPDATA%`, y a memoria si no se puede escribir. La redacción no es exhaustiva, y crash logs e informes pueden contener datos personales. Salida por proceso limitada y lectura concurrente; se sigue drenando el pipe después de truncar.
- Se añadieron atajos, etiquetas de estado y recursos de alto contraste sin validación de Narrator/DPI en Windows. Pruebas automatizadas cubren política de autorización, diagnóstico, redacción y límites de salida además de ejecutor y paquetes.

## Pendiente

1. Auxiliar elevado restringido y autenticado; eliminar la elevación de toda la UI.
2. Rollback real por tipo de operación y recuperación de estados parciales; rediseñar acciones suspendidas antes de habilitarlas.
3. Completar diagnóstico por categoría, interpretación localizada y el informe estructurado con cobertura real.
4. Revisar el resto de rutas de comandos, ejecutables de terceros, clasificación lectura/escritura y privacidad; validar accesibilidad y tratamiento de archivos compartidos.
5. Compilar y ejecutar las pruebas, smoke test y escenarios de restauración/UAC/simulación/errores/cierre en VMs desechables Windows 10/11. No se han ejecutado reparaciones reales en esta sesión.

Las verificaciones estáticas no sustituyen la compilación ni las pruebas en Windows.
