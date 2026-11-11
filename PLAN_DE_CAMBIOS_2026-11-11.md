# Plan de cambios de WDigger

**Fecha:** 11 de noviembre de 2026

**Base analizada:** `ab8396adced2f4abf39e10718571c10c28a46ff7` (`main`)

**Método:** revisión estática de la solución WinUI 3, módulos, manifiesto, políticas, pruebas y CI. Se aplicaron las guías adjuntas `winrepair-core`, `winrepair-ui`, `winrepair-ux`, `winrepair-ix` y `unslop`.

> Este documento es un plan técnico, no una certificación de seguridad ni una prueba funcional. No se ejecutaron reparaciones, UAC, WMI, Windows Update, drivers ni restauraciones en una VM Windows durante esta revisión. Las acciones que cambian Windows no deben ampliarse hasta completar los criterios de aceptación indicados.

## 1. Resumen ejecutivo

WDigger ya cuenta con buenas barreras iniciales: manifiesto `requireAdministrator`, comprobación del token elevado, lista de herramientas del sistema resueltas desde `System32`, confirmaciones, bloqueo de operaciones simultáneas, simulación, captura concurrente de salida, límite de salida, punto de restauración y reversión acotada de tres valores de registro. El CI de Windows compila, ejecuta las pruebas de seguridad y publica un EXE portable.

Aun así, el producto no está listo para presentar sus módulos como un flujo completo de diagnóstico y reparación:

1. **La arquitectura eleva toda la UI desde el arranque.** Es el requisito actual del producto, pero contradice el mínimo privilegio recomendado por las skills. Amplía la superficie de WinUI, configuración, pickers y dependencias.
2. **El diagnóstico real es mínimo.** `DiagnosticService` solo evalúa espacio de disco, adaptadores activos y presencia de Windows; nueve categorías se informan como no disponibles. Las reparaciones no están vinculadas a un hallazgo concreto.
3. **Falta un contrato de operación.** Las acciones devuelven `Task` y el shell no distingue de forma tipada entre no iniciada, simulada, correcta, parcial, cancelada, reinicio requerido o intervención manual. Por ello, un retorno normal después de una cancelación interna puede acabar con el mensaje genérico de acción terminada.
4. **Las operaciones reales no tienen recuperación homogénea.** El registro conserva estado previo; Windows Update intenta compensación en memoria. Appx, SFC, DISM, activación y otros cambios no exponen una estrategia de resultado parcial, recuperación posterior o reinicio.
5. **Quedan rutas de ejecución a normalizar.** Hay una API de argumentos como cadena y varios módulos la usan; además persisten scripts PowerShell generados en memoria. Aunque los parámetros actuales están acotados en buena parte, no es el contrato tipado y sin shell exigido por `winrepair-core`.
6. **La UI es una consola de acciones, no una vista de hallazgos priorizados.** Tiene `NavigationView`, log colapsable y atajos, pero faltan estado por comprobación, detalles técnicos expandibles, asociación hallazgo→reparación, anuncios completos a Narrator y validación en DPI/alto contraste.

La prioridad no es habilitar más botones: es convertir las acciones existentes en operaciones verificables, recuperables y explicables. Las funciones ya suspendidas (Acronis, WinFR, Winget) deben permanecer así hasta pasar una revisión específica.

## 2. Decisión de producto que bloquea el diseño

Las skills de core, UX e IX recomiendan UI sin elevar y un auxiliar elevado de lista cerrada bajo demanda. La rama actual exige `requireAdministrator` para toda la aplicación por un requisito previo explícito. Ambas posturas no pueden cumplirse simultáneamente.

### Decisión requerida: modelo de privilegios

| Opción | Consecuencia | Recomendación técnica |
|---|---|---|
| **A. Mantener elevación al inicio** | Cumple el requisito actual; UAC rechazado impide abrir incluso diagnósticos. | No habilitar más acciones reales hasta endurecer el host elevado, la configuración y las rutas externas. Tratar todos los módulos como superficie privilegiada. |
| **B. Migrar a UI `asInvoker` + helper elevado** | Cumple el mínimo privilegio de las skills; UAC aparece solo al reparar y el rechazo degrada con gracia. | Objetivo recomendado a medio plazo. Requiere aceptar un paquete portable de varios binarios firmados o definir extracción segura de un helper. |

**Plan por defecto:** ejecutar las fases 0–2 sin cambiar el comportamiento de elevación actual. Antes de la fase 3, aprobar A como arquitectura final o B como migración. No se debe cambiar el manifiesto unilateralmente: afectaría al requisito de producto vigente y al flujo de soporte.

## 3. Estado frente a las skills

| Área | Estado actual | Brecha principal | Prioridad |
|---|---|---|---|
| Core: ejecución | `ArgumentList`, pipes concurrentes, límite de salida y resolución de herramientas de Windows presentes. | Sigue existiendo `RunCommandAsync(string arguments)`; modificaciones desactivan el timeout general; scripts PowerShell se construyen como texto. | P0 |
| Core: privilegios | Manifiesto y guardia de token elevados; se bloquea `PATH` para nombres no confiables. | Toda la UI queda elevada; la API aún acepta rutas absolutas y no hay broker con allowlist independiente. | P0 / decisión |
| Core: rollback | Punto de restauración intentado y snapshot de registro verificado. | Sin journal persistente por operación ni compensación uniforme tras cierre, reinicio o fallo parcial. | P0 |
| Diagnóstico | Informe tipado, ordenado por severidad; tres chequeos efectivos. | Nueve categorías declaradas no disponibles; no preflight ni vínculo con reparaciones. | P1 |
| UX | Confirmación global con reconocimiento para cambios y aviso si falta el punto de restauración. | No hay flujo diagnóstico→reparación ni resultado final tipado por operación. | P1 |
| UI | `NavigationView`, tema, panel de salida, tabla y estado en vivo básicos. | Falta una lista de hallazgos, detalles expandibles, iconografía/estado accesible y validación a 100–200 % DPI. | P1 |
| IX | F5, Ctrl+R, Ctrl+L, Esc y F1; autoscroll condicionado a estar al final. | Sin progreso por pasos/porcentaje de herramientas, política de animación reducida ni pruebas Narrator. | P2 |
| Auditoría | JSONL rotado, redacción básica y límite de memoria. | Sin ID de operación, contexto de sistema/privilegio/resultados estructurados ni exportación de soporte con consentimiento. | P1 |
| Calidad y limpieza | Pruebas de ejecutor, política, diagnóstico, registro y manifiesto; CI Windows pasa el flujo de build/publish. | No hay tests de UI/UAC/VM ni smoke test interactivo dentro de CI; legado de configuración e historial no usado por el flujo actual. | P1 / P3 |

## 4. Hallazgos y plan priorizado

### P0-01 — Definir resultados y estado persistente de cada operación

**Evidencia actual**

- `ModuleAction` expresa principalmente booleanos (`ChangesSystem`, `CanCancel`, `AllowedInElevatedSession`) y las acciones devuelven `Task`.
- `ShellWindow.RunActionAsync` finaliza con un mensaje genérico cuando la tarea retorna; no recibe un estado de negocio.
- `RepairModule.ResetWindowsUpdateAsync` compensa renombres y servicios dentro del mismo script, pero no deja un journal que una sesión posterior pueda retomar.
- Solo `RegistryTweakService` guarda un estado previo explícito y lo verifica al revertir.

**Cambio**

1. Crear el dominio `Operations` separado de WinUI: `OperationDefinition`, `OperationPlan`, `PreflightResult`, `OperationStep`, `OperationOutcome` y `RecoveryRecord`.
2. Usar estados exclusivos: `NotStarted`, `Simulated`, `Succeeded`, `SucceededWithRestartRequired`, `Failed`, `Partial`, `Cancelled`, `Blocked`, `ManualActionRequired`.
3. Crear un ID de operación al confirmar; persistir el plan, preflight, punto de restauración, pasos iniciados/finalizados y resultado en un journal atómico.
4. Exigir que cada operación real declare: riesgo, cancelabilidad, timeout/política de espera, reinicio posible, datos previos requeridos y recuperación disponible/no disponible.
5. Cambiar el shell para presentar el `OperationOutcome`, no inferir éxito porque no se lanzó una excepción.

**Aceptación**

- Un fallo, cancelación, rechazo de diálogo interno o cierre no se muestra como éxito.
- Al reiniciar WDigger se detecta una operación incompleta y se ofrecen instrucciones seguras o recuperación aprobada.
- Los informes separan correctamente correcto, parcial, cancelado, simulado, reinicio y manual.

### P0-02 — Contener operaciones reales sin recuperación demostrada

**Evidencia actual**

Las acciones revisadas y habilitadas incluyen SFC, DISM RestoreHealth, CHKDSK `/scan`, reset de Windows Update, vaciado DNS, activación de Windows, retirada Appx y ajustes de registro. Windows Update es una secuencia de servicios y renombres; Appx no tiene reinstalación/recuperación de datos garantizada. La creación de punto de restauración puede fallar y hoy permite continuar mediante confirmación.

**Cambio**

1. Antes de ampliar el catálogo, clasificar cada acción habilitada como `lectura`, `cambio reversible`, `cambio parcialmente reversible` o `irreversible`.
2. Suspender por defecto las operaciones de riesgo alto cuya recuperación no esté diseñada, en particular reset de Windows Update y retirada Appx, hasta implementar preflight y un resultado parcial persistente.
3. Mantener SFC/DISM como operaciones de reparación, no como diagnóstico; añadir preflight, fuente de reparación explícita cuando aplique y una salida clara de reinicio/manual.
4. Validar por versión de Windows el comportamiento exacto de `chkdsk /scan`; ajustar texto, riesgo y necesidad de reinicio a evidencia de VM, no a una suposición genérica.
5. Requerir confirmación final específica para continuar sin punto de restauración, con el motivo del fallo registrado en el journal.

**Aceptación**

- Ninguna operación de riesgo alto se ejecuta solo porque el usuario pulsó una tarjeta.
- Cada operación habilitada dispone de una matriz de preflight, rollback/compensación, límites y escenarios de fallo probados en VM.
- Acronis, WinFR, Winget/Chocolatey y acciones no revisadas siguen deshabilitadas, también si se modifican JSON externos.

### P0-03 — Sustituir el ejecutor de cadenas por especificaciones tipadas

**Evidencia actual**

`CommandExecutor` captura correctamente salida y usa `ArgumentList` cuando se le entrega una lista, pero expone `RunAsync(fileName, arguments)` y `ProcessStartInfo.Arguments`. Módulos de red, disco, drivers, eventos, recuperación y reparación todavía lo usan. El limpiador temporal utiliza `cmd /c`; varias consultas usan `powershell -Command`; algunos scripts se interpolan y codifican en `ModuleExecutionContext`.

**Cambio**

1. Deprecar y eliminar la API pública de argumentos como cadena; usar `CommandSpec` con ejecutable lógico, lista inmutable de argumentos, tipo (`ReadOnly`/`Modify`), timeout y política de cancelación.
2. Reemplazar `cmd /c` por una implementación .NET que enumere, previsualice y borre temporales sin seguir reparse points, o mantenerla suspendida.
3. Convertir consultas estáticas PowerShell en recursos embebidos/versionados con parámetros tipados; usar `-NoProfile`, `-NonInteractive` y la política acordada. No aceptar script arbitrario de módulos o configuración.
4. Convertir comandos fijos restantes a `ArgumentList`; validar semánticamente entradas del usuario (adaptador existente, volumen, destino, dispositivo) antes de invocarlos.
5. Reducir `ResolveTrustedExecutable` a descriptores aprobados; no aceptar rutas absolutas genéricas en producción. Las integraciones externas solo se habilitan tras validar ubicación, firma, versión y contrato.

**Aceptación**

- No queda ningún uso de `ProcessStartInfo.Arguments`, `RunCommandAsync(string, string)` ni `cmd /c` en rutas de producción.
- Tests cubren comillas, espacios, Unicode, salida masiva, proceso hijo, timeout, cancelación y una ruta externa no autorizada.
- Un módulo no puede solicitar un ejecutable, script o switch fuera de la allowlist.

### P0-04 — Política de timeout, cancelación y cierre seguro

**Evidencia actual**

El ejecutor tiene un timeout por defecto de 30 minutos, pero `ModuleExecutionContext` lo desactiva para toda acción marcada como cambio de sistema. La UI evita cerrar mientras hay una operación y advierte al cancelar cambios, pero no tiene una política por herramienta ni pasos de limpieza recuperables.

**Cambio**

1. Definir timeout por operación y fase: preflight corto, SFC/DISM configurable con aviso de espera, Windows Update por servicio/paso y operaciones no cancelables con estado explícito.
2. Modelar cancelación como una política: permitida, confirmable, no permitida o requiere esperar; no inferirla solo desde `CanCancel`.
3. Al pedir cancelación, almacenar la intención y el paso activo antes de terminar el árbol de procesos; ejecutar compensación solo si está declarada como segura.
4. Mantener el bloqueo de cierre, pero mostrar tiempo transcurrido, paso actual y alternativa de esperar; no prometer que `Kill` deja Windows consistente.

**Aceptación**

- Cada operación de modificación tiene límite documentado o una razón técnica para no tenerlo, más una ruta de escalado manual.
- Cancelar un proceso no deja la UI ni el informe en estado ambiguo.
- Tests inyectan timeout/cancelación entre los pasos de Windows Update y verifican el journal final.

### P1-01 — Construir un diagnóstico real y ligar reparación a evidencia

**Evidencia actual**

`DiagnosticService` informa sistema operativo, espacio libre y adaptadores activos. Integridad, imagen, sistema de archivos, arranque, drivers, registro, rendimiento, seguridad y eventos se devuelven como no disponibles. Los módulos operativos son independientes de ese informe.

**Cambio**

1. Crear un catálogo de comprobaciones con ID estable, categoría, requisitos, severidad, evidencia técnica, fecha, privilegio y acción recomendada.
2. Implementar primero lecturas de alto valor y bajo riesgo: DISM CheckHealth/ScanHealth, estado del volumen, BCD enumerado, DNS/adaptadores, firewall/Defender, drivers con error y eventos críticos filtrados por ventana temporal.
3. Declarar de forma honesta `Unavailable`, `AccessDenied`, `NotApplicable` y `Failed`; no convertir una excepción WMI en un resultado sano.
4. Exigir un hallazgo compatible y reciente antes de habilitar una reparación recomendada, con una ruta avanzada separada y claramente marcada para técnicos.
5. Incluir en el informe un resumen legible, evidencia redactada, recomendaciones y el enlace con cualquier operación ejecutada.

**Aceptación**

- La pantalla principal ordena hallazgos por severidad/impacto y muestra icono, texto y recomendación.
- Un equipo sano, una advertencia, un error, permiso denegado y una comprobación no aplicable se distinguen en UI, informe y log.
- Una reparación no aparece como respuesta recomendada si no existe una comprobación asociada.

### P1-02 — Journal, rollback y almacenamiento confiable

**Evidencia actual**

Los registros JSONL tienen rotación y redacción básica; las salidas completas no se almacenan. `TweakHistoryService` y `ConfigManager` aún prefieren la carpeta portable, aunque el logger puede usar `%LOCALAPPDATA%`. `TweakHistoryService` es legado: la ruta actual de ajustes no lo actualiza y la reversión real usa el snapshot en HKLM.

**Cambio**

1. Definir una única estrategia de datos: portable cuando sea seguro, y fallback consistente para logs, journal, ajustes de UI e informes cuando el medio es de solo lectura.
2. Persistir operaciones y snapshots con escritura atómica, esquema versionado, retención y detección de corrupción.
3. Retirar o migrar `TweakHistoryService`, `TweakDefinition` y `config/tweaks.json` si no forman parte del flujo actual; no conservar una interfaz que parezca operativa pero no gobierne ajustes reales.
4. Para red, drivers, dispositivos y archivos, capturar un estado previo solo cuando la reversión sea técnicamente fiable; si no lo es, declararlo antes de ejecutar.
5. Registrar punto de restauración con identificador/fecha y no presentarlo como rollback garantizado.

**Aceptación**

- Una aplicación desde USB de solo lectura conserva logs/journal en el fallback y la UI muestra la ubicación real.
- No hay dos fuentes de verdad contradictorias para ajustes o historial.
- Interrumpir una operación en cada paso deja un registro legible y una instrucción/reversión verificable.

### P1-03 — Auditoría, privacidad e informes de soporte

**Evidencia actual**

`AppLogger` registra texto JSONL con fecha, severidad y categoría; oculta IP y rutas de perfil de forma limitada. Registra comando, código y duración, pero no ID de operación, versión, SO, arquitectura, estado de elevación, pasos ni resultado tipado. El informe de diagnóstico es texto plano y no incorpora operaciones. `CrashLog` puede contener excepciones sin la misma redacción.

**Cambio**

1. Introducir eventos estructurados y correlacionados: `operationId`, definición/versionado, usuario no identificable, elevación, host/OS redactado, inicio/fin, pasos, exit code, estado y reinicio requerido.
2. Separar log operativo mínimo de detalle de soporte. La salida completa solo se conservará con consentimiento explícito, límite, redacción y aviso antes de exportar.
3. Unificar redacción y retención para JSONL, informes y crash logs; revisar nombres de equipos, MAC, seriales, rutas, licencias y argumentos sensibles.
4. Añadir exportación de informe de operación y diagnóstico con resumen primero, detalles técnicos opcionales después.

**Aceptación**

- Un informe de soporte permite saber qué ocurrió sin afirmar salud/reparación no verificada.
- Los datos de red y perfil no aparecen por defecto en los artefactos exportados.
- Los registros de un mismo flujo se pueden correlacionar sin depender de texto libre.

### P1-04 — Validación de módulos y contrato de terceros

| Módulo | Cambio antes de habilitar más acciones reales |
|---|---|
| Reparación | Diagnóstico previo, preflight, interpretación localizada y journal para SFC/DISM/CHKDSK/Windows Update. |
| Red y red avanzada | Inventariar adaptadores antes de pedir texto; snapshot de DNS/IP, detección de sesión remota y resultado de reinicio. |
| Disco y limpieza | Sustituir `cmd /c`; previsualizar alcance, excluir reparse points/archivos críticos y mostrar bytes/errores. |
| Controladores y dispositivos | Verificar firma, proveedor, compatibilidad y dispositivo estable; no habilitar instalación/deshabilitación por texto libre sin una selección inventariada. |
| Software | Mantener Winget/Chocolatey suspendidos; definir verificación de fuente, firma, versión y consentimiento por paquete. |
| Activación | Separar licencia/estado de reparación; no presentar activación como solución técnica; revisar rutas/compatibilidad Office. |
| Appx | Mantener la selección estricta, pero añadir preflight, impacto/datos, estado de paquete y resultado verificable; no prometer rollback. |
| Acronis y WinFR | Mantener suspendidos. Requieren identificación de discos físicos, exclusión de origen=destino, validación de herramienta/versiones y confirmación final. |
| Eventos, BIOS y hardware | Aplicar filtros de periodo/severidad y distinguir una excepción de WMI de un hallazgo. |

**Aceptación**

- Cada acción expuesta tiene propietario, contrato de entrada/salida, nivel de riesgo, pruebas de VM y decisión explícita de habilitada/suspendida.
- Las acciones no habilitadas no tienen rutas alternativas que puedan ejecutarlas mediante configuración o argumentos externos.

### P2-01 — Rediseño de UI y UX orientado a hallazgos

**Cambio**

1. Conservar `NavigationView`, pero hacer que el centro de la aplicación sea el resumen de diagnóstico y no las tarjetas de comandos.
2. Mostrar por comprobación estado textual e icono; usar color solo como apoyo. Ordenar por error, impacto y categoría.
3. Incorporar panel expandible de detalles técnicos, evidencia, salida acotada, código de retorno y recomendación.
4. Mostrar acción recomendada y alternativas por hallazgo; antes de “Reparar seleccionadas”, enumerar acciones, riesgo, irreversibilidad, reinicios y punto de restauración.
5. Diseñar el resultado final como informe: encontrado, ejecutado, resultado, pendientes y próximos pasos.
6. Sustituir los colores creados en código de `ActionCardViewModel` y `TableRenderer` por `ThemeResource`/recursos de alto contraste. Revisar el `ItemsWrapGrid` de tamaño fijo y el panel inferior a 100 %, 125 %, 150 % y 200 %.

**Aceptación**

- Un usuario entiende el problema, el impacto y la acción recomendada sin leer una consola.
- Alto contraste conserva contraste, foco y semántica sin depender de colores hardcodeados.
- No hay truncamiento crítico ni tarjetas inoperables a 200 % DPI o con escalado de texto aumentado.

### P2-02 — Interacción, accesibilidad y progreso

**Cambio**

1. Mantener F5, Ctrl+R, Ctrl+L, Esc y F1, pero hacer Ctrl+R ejecutar una selección explícita de reparaciones aprobadas o renombrarlo si solo abre información.
2. Añadir `AutomationProperties.Name`, `HelpText` y regiones vivas para cambios de estado, resultados, errores, reinicio y cancelación; probar Narrator con teclado completo.
3. Traducir el progreso de DISM y operaciones por pasos a porcentaje/estado; mantener indeterminado solo cuando no sea posible medir.
4. Respetar la preferencia de reducir animaciones; conservar el comportamiento actual de no auto-scroll cuando el usuario no está al final.
5. Evaluar bandeja y toast solo después de tener journal, resultado final y ciclo de vida seguro; no son parte del P0/P1.

**Aceptación**

- Cada flujo principal se completa con teclado y Narrator sin pérdida de foco.
- Cancelación indica si es segura, no permitida o puede dejar cambios parciales.
- El log conserva la posición de lectura y el estado se anuncia una sola vez, sin ruido.

### P3-01 — Limpieza controlada (`unslop`) y mantenibilidad

**Evidencia actual**

Hay comentarios útiles que explican riesgos (pipes, restauración, UAC), pero también comentarios obvios, helpers de paso y superficies heredadas. `ConfigManager.Tweaks`, `TweakDefinition` y los métodos de escritura de `TweakHistoryService` no gobiernan la ruta actual de ajustes; `CommandExecutor.Shorten` y `AppLogger.Debug` requieren confirmar uso antes de conservarlos.

**Cambio**

1. Tras migrar el contrato de operaciones, eliminar código y JSON legado no referenciado; no borrar snapshots de registro ni comentarios que documenten una restricción de seguridad.
2. Reducir comentarios que solo repiten el código y conservar los que expliquen un riesgo, una incompatibilidad Windows o una decisión de recuperación.
3. Unificar helpers de módulos para que no oculten strings de comandos y normalizar nombres/formatos.
4. Añadir analizadores y reglas de build para imports, miembros no usados, nulabilidad y documentación de APIs públicas críticas.

**Aceptación**

- La limpieza no cambia comportamiento ni elimina contexto de seguridad.
- No queda configuración editable que sugiera acciones que el producto ignora.
- La solución compila sin nuevas advertencias y las pruebas cubren las eliminaciones.

## 5. Secuencia de entrega propuesta

| Hito | Dependencias | Entregable | No habilitar todavía |
|---|---|---|---|
| 0. Decisión de privilegios | Aprobación de producto | ADR: mantener elevación global o migrar a helper | Nuevas funciones reales |
| 1. Contención | Ninguna | Inventario de acciones, estado tipado mínimo, suspensión de riesgo alto, contrato de timeout/cancelación | Windows Update/Appx sin journal |
| 2. Núcleo | Hito 1 | `CommandSpec`, `OperationOutcome`, journal, preflight y datos consistentes | Rutas de strings, shell o binarios externos |
| 3. Diagnóstico | Hito 2 | Catálogo real de comprobaciones e informe correlacionado | Reparar sin hallazgo asociado |
| 4. Módulos | Hitos 2–3 | Un módulo por vez con VM, rollback/limitaciones y pruebas | Acronis, WinFR, Winget/Chocolatey sin validación |
| 5. UX/UI/IX | Hitos 2–3 en paralelo | Hallazgos, detalles, accesibilidad, DPI/alto contraste y progreso | Toast/bandeja antes de resultado seguro |
| 6. Publicación | Todos los anteriores | Matriz de validación, smoke interactivo, criterios de release y guía de soporte | Afirmaciones de producción sin evidencias |

## 6. Matriz mínima de validación

Las pruebas destructivas se ejecutarán solo en VMs desechables con snapshot. CI no debe ejecutar SFC, DISM, CHKDSK, reset de red, registro, drivers, Windows Update, Appx ni borrado real.

| Escenario | Evidencia requerida |
|---|---|
| Windows 10 y Windows 11 soportados | Build, arranque, diagnóstico, informe y rutas de datos. |
| UAC aceptado/rechazado | Según la decisión A/B: inicio bloqueado documentado o UI degradada con helper. |
| Restauración del sistema activa/inactiva | Creación verificada, rechazo y continuación explícita, journal. |
| Sin red / Windows Update fallido | Resultado parcial/manual; servicios y datos en estado documentado. |
| Medio portable de solo lectura | Configuración, fallback de logs/journal y bloqueo de cambios si no se puede auditar. |
| Error/cancelación/cierre forzado | Cada paso recuperable o instrucción manual persistente. |
| Windows español e inglés | Interpretación de herramientas no basada solo en frases inglesas. |
| DPI 100–200 %, alto contraste, texto ampliado, Narrator | Foco, anuncios, contraste, truncamiento y teclado. |
| Salidas grandes, silenciosas y procesos árbol | Límite, timeout, cancelación y no bloqueo de pipes. |

## 7. Decisiones que deben aprobarse antes de implementar

1. ¿Se mantiene obligatoriamente el EXE único y elevado al inicio, o se acepta un helper elevado firmado junto a la UI?
2. ¿Cuáles de las acciones actuales se consideran parte del producto soportado? En particular: Windows Update, Appx, activación, limpieza, red, drivers y dispositivos.
3. ¿Cuándo se permite continuar sin punto de restauración y para qué riesgos?
4. ¿Dónde se guardan journal, logs e informes en USB de solo lectura, y cuál es la política de retención/privacidad?
5. ¿Qué versiones exactas de Windows, Office, Windows App SDK y herramientas externas se soportarán?
6. ¿Qué datos técnicos pueden entrar en el informe de soporte y bajo qué consentimiento?

## 8. Resultado de la revisión

La base actual es más segura que un lanzador de comandos sin controles, pero todavía es una aplicación elevada con módulos heterogéneos y diagnóstico incompleto. El siguiente cambio recomendado es el **hito 0 (decisión de privilegios) junto con P0-01 a P0-04**. No se recomienda habilitar operaciones adicionales ni declarar la aplicación lista para producción antes de completar esa contención y validarla en VMs Windows.
