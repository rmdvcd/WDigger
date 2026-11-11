# Auditoría de WDigger y plan de cambios

**Fecha:** 10 de noviembre de 2026

**Base revisada:** `6bdb782a4af3035a7be9e3aec43e83b25ddf121e`

**Criterios:** skills adjuntas `winrepair-core`, `winrepair-ux`, `winrepair-ui` y `winrepair-ix`.

> **Actualización de implementación — 11 de noviembre de 2026:** por el requisito operativo actual, la rama de trabajo usa `requireAdministrator` y valida el token elevado al arrancar. Esto sustituye las referencias de este informe a un manifiesto `asInvoker` como descripción del estado presente; no invalida la recomendación de P1 de volver a mínimo privilegio mediante un auxiliar restringido. Las integraciones de Winget y WinFR se mantienen suspendidas en sesiones elevadas hasta verificar de forma independiente la ruta y la firma de sus binarios.
>
> Este archivo conserva la auditoría previa. El plan revalidado sobre el commit actual está en [PLAN_DE_CAMBIOS_2026-11-11.md](PLAN_DE_CAMBIOS_2026-11-11.md).

## 1. Resumen ejecutivo

WDigger dispone de una base WinUI 3 razonable para una herramienta portable: navegación por módulos, indicadores de acciones, salida integrada, bloqueo de ejecución simultánea y confirmaciones para muchas operaciones. No obstante, su arquitectura actual es principalmente un **lanzador de utilidades**, no un flujo de diagnóstico estructurado previo a la reparación. Las cuatro skills exigen cambios sustanciales en privilegios, ejecución, reversibilidad, resultados y accesibilidad.

**Conclusión:** no se recomienda confiar en las operaciones de modificación o restauración en equipos de producción hasta corregir y probar los hallazgos P0. El mayor riesgo no es solo una posible operación peligrosa, sino que la aplicación puede **comunicar éxito después de un fallo**, continuar una secuencia parcialmente fallida o ofrecer una reversión que no devuelve el estado anterior.

Prioridad propuesta:

1. **P0 — Contener daño y falsos éxitos:** deshabilitar temporalmente restauración de imágenes y ajustes arbitrarios, y proteger secuencias de reparación, simulación y cancelación.
2. **P1 — Rediseñar la frontera privilegiada y la recuperación:** aplicación sin elevar, auxiliar restringido, estado previo y punto de restauración verificable cuando proceda.
3. **P1 — Crear diagnóstico y resultados tipados:** análisis previo, errores verificables e informe exportable.
4. **P2 — Completar UX, accesibilidad, telemetría local y validación en Windows.**

## 2. Alcance, método y límites

- Revisión estática del proyecto C#/WinUI, sus 13 módulos, configuración JSON, publicación y CI. Se rastrearon las rutas de ejecución, confirmación, restauración, simulación, salida y cancelación.
- No se ha ejecutado la aplicación ni se han realizado reparaciones: el entorno de auditoría es Linux, no tiene `dotnet` instalado y no permite validar WinUI, UAC, WMI ni herramientas nativas de Windows. Las conclusiones de código son estáticas; los aspectos visuales, de interoperabilidad y comportamiento de herramientas externas requieren pruebas en Windows.
- No se ha realizado pentest, análisis exhaustivo de dependencias, validación de firma, ni inspección real de un artefacto publicado. No debe interpretarse este informe como certificación de seguridad.
- Las skills son un marco de referencia; no todos los comandos etiquetados como lectura por una guía pueden ejecutarse sin privilegios en todas las versiones o políticas de Windows. Hay que probar cada comprobación y mostrar «requiere permisos/no disponible» sin elevar silenciosamente.
- Los puntos de restauración de Windows **no son copias de seguridad de datos**, pueden estar deshabilitados o ser limitados por políticas/frecuencia y no garantizan revertir todos los cambios. Las operaciones destructivas necesitan además salvaguardas específicas.
- P0 = bloqueo inmediato antes del uso real; P1 = alta prioridad antes de una versión segura; P2 = mejora funcional, accesibilidad o mantenibilidad. La prioridad refleja riesgo potencial, no una explotación demostrada.

## 3. Hallazgos priorizados

### P0-01 — Resultados incorrectos y secuencias que continúan tras fallar

**Evidencia:** `src/WDigger/UI/ModuleExecutionContext.cs:68-84` devuelve `CommandResult` sin lanzar error por código distinto de cero; `UI/ShellWindow.xaml.cs:536-545` muestra «Acción finalizada» al retornar la tarea. `Modules/RepairModule.cs:71-92` ignora los códigos de parada, renombrado y arranque de servicios y termina con mensaje de éxito. `Modules/TweaksModule.cs:61-74,113-123` ignora resultados y registra o elimina historial igualmente. `Modules/NetworkModule.cs:54-61` y `Modules/AdvancedNetworkModule.cs:90-93` también muestran éxito sin comprobar resultado.

**Impacto:** un fallo parcial puede dejar servicios detenidos, DNS a medias o ajustes no aplicados, al tiempo que el historial o la UI afirman lo contrario. Cancelar durante Windows Update también puede impedir alcanzar el bucle que reinicia servicios.

**Cambio:** contrato tipado de resultados por paso y operación (`Correcto`, `Fallido`, `Parcial`, `Cancelado`, `Simulado`, `Requiere reinicio`, `Manual`); evaluar códigos específicos por herramienta, detener secuencias ante fallos y ejecutar compensación/limpieza segura en `finally`. Registrar el historial solo tras verificar el resultado y conservar estado parcial para recuperación. Desactivar temporalmente la reparación de Windows Update hasta demostrar limpieza en fallos y cancelaciones.

### P0-02 — Comandos arbitrarios desde configuración editable y proceso elevado

**Evidencia:** `Core/ConfigManager.cs:38-52` da prioridad a JSON externo; `Modules/TweaksModule.cs:146-153` transforma cada acción en `cmd /c` y acepta `action.Command` del JSON; `app.manifest:8-9` y `App.xaml.cs:24-26,79-125` exigen administrador. El escape en `TweaksModule.cs:165-172` duplica barras y usa `\"`, lo que no constituye una validación de semántica ni un mecanismo general de escape para `cmd.exe`. `SoftwareModule.cs:115-121` incorpora ID y argumentos editables a una línea de comando.

**Impacto:** si una configuración situada junto a la copia portable se modifica antes de que un técnico apruebe y ejecute un ajuste, puede dirigir ejecución elevada no prevista; también son posibles argumentos alterados y fallos de rutas/comillas. Es una **superficie de riesgo condicionada a la modificación de configuración y ejecución por el usuario**, no una explotación remota demostrada. El uso de nombres de ejecutables sin ruta absoluta merece además revisión de resolución de binarios y directorios confiables.

**Cambio:** hasta rediseñar, deshabilitar ajustes con `command` y opciones arbitrarias de paquetes. Adoptar operaciones tipadas con identificadores y parámetros validados, sin `cmd /c` ni interpolación de entrada en scripts; registro mediante API nativa; `ProcessStartInfo.ArgumentList` y binarios del sistema resueltos por ubicación confiable. Scripts PowerShell propios embebidos, no interactivos, con parámetros separados y restricciones explícitas. El auxiliar elevado debe validar por sí mismo operaciones y parámetros; firmar o verificar integridad y proteger ACL/origen de binarios y configuración. `ArgumentList` no sustituye validar el lenguaje de un shell ni los argumentos del programa de destino.

### P0-03 — Restauración de imagen: confirmación antes de conocer el destino

**Evidencia:** `Modules/BackupModule.cs:18-20,68-93` advierte de sobrescritura, pero el diálogo central se muestra antes de seleccionar imagen y unidad (`ShellWindow.xaml.cs:502-515`). Después se ejecuta `deploycmd` sin confirmación final sobre el destino concreto ni comprobación visible de compatibilidad de herramienta y sintaxis.

**Impacto:** posible pérdida irreversible de datos si el destino elegido es incorrecto. System Restore no protege la imagen ni los archivos sobrescritos.

**Cambio:** deshabilitar la ejecución hasta validarla en VM con la versión exacta de Acronis. Antes de ejecutar, mostrar imagen, volumen/disco físico, capacidad, nombre y contenido afectado; evitar por defecto el disco de sistema, comprobar compatibilidad y copia independiente, exigir checkbox y confirmación inequívoca del destino. No prometer rollback automático.

### P0-04 — Cancelación y timeout pueden dejar cambios incompletos

**Evidencia:** `Core/CommandExecutor.cs:89-102` consume stdout hasta EOF **antes** de iniciar la espera de timeout; un proceso que permanece vivo con stdout abierto puede no llegar nunca al temporizador. En `:116-128,175-182` la cancelación mata el árbol de procesos; `ShellWindow.xaml.cs:470-481,589-594,664-680` permite cancelar o cerrar sin política específica por operación. La limpieza de Windows Update solo ocurre al final normal (`RepairModule.cs:71-90`).

**Impacto:** bloqueo prolongado y/o interrupción de procesos en una fase no segura, con cambios parciales. El comportamiento concreto depende de la herramienta.

**Cambio:** iniciar simultáneamente lectura de ambos flujos, espera de salida, timeout y cancelación; establecer límites configurables por operación, política explícita de cancelación y estados intermedios. Para operaciones no interrumpibles, impedir cancelación forzosa desde UI y explicar cómo proceder; diseñar compensación segura y reanudación. Probar también cierre de ventana, proceso silencioso y procesos descendientes.

### P0-05 — La simulación no es una barrera completa de escritura

**Evidencia:** `Core/CommandExecutor.cs:47-58` simula **todos** los comandos, incluso los de lectura, devolviendo código 0; `Modules/DriversModule.cs:32-66` crea realmente el directorio de destino antes de pasar por el ejecutor. El interruptor puede cambiar durante la operación (`ShellWindow.xaml.cs:246-259`), ya que no se ve un bloqueo de la barra superior en `:589-595`.

**Impacto:** una operación de lectura no aporta datos en simulación; la escritura directa escapa a la barrera, y cambiar el modo durante una secuencia puede producir resultados mixtos. La presencia de registros/configuración propios también debe explicarse claramente en la definición de simulación.

**Cambio:** capturar un contexto inmutable de ejecución al inicio; simular únicamente efectos declarados, permitiendo diagnósticos realmente de solo lectura. Encapsular **todas** las escrituras del destino en servicios comprobables, incluida creación de carpetas; separar explícitamente datos propios de WDigger de modificaciones del sistema. Resultado `Simulado` nunca equivale a éxito real. Bloquear el selector de modo durante la operación y probar con dobles que no ocurren efectos externos.

### P1-01 — Privilegios globales y UAC sin degradación

**Evidencia:** `app.manifest:8-9`, `App.xaml.cs:79-125`, `Core/AppHost.cs:40-44` y `UI/ShellWindow.xaml:31-35`. Se exige administrador antes de abrir y se sale si se rechaza; no existe auxiliar restringido. CI verifica específicamente `requireAdministrator` (`.github/workflows/portable-windows.yml:75-81`).

**Impacto:** incluso inventario y navegación corren elevados; al rechazar UAC no hay diagnóstico disponible. Aumenta la superficie de cualquier error en UI, configuración o dependencias.

**Cambio:** principal `asInvoker`; determinar permisos reales por comprobación; auxiliar elevado bajo demanda con lista cerrada de operaciones, autenticación/autorización del canal IPC, parámetros validados y duración limitada. Explicar la elevación, tratar rechazo como informativo y permitir continuar con las comprobaciones disponibles. Resolver el conflicto con el requisito actual de EXE único mediante una decisión de diseño documentada y pruebas de integridad/extracción segura; no suponer que cambiar el manifiesto basta. Actualizar la verificación de CI.

### P1-02 — Protección y rollback incompletos

**Evidencia:** solo los ajustes invocan `RestorePoints.Create` (`TweaksModule.cs:51-59,106-110`); si falla se continúa sin nueva autorización explícita. No se captura estado previo. `config/tweaks.json` revierte a valores prefijados —p. ej. plan equilibrado o telemetría a `1`—, no al valor previo. `Services/TweakHistoryService.cs:28-37,69-79` solo guarda ID/nombre/fecha e ignora errores de persistencia. `Core/RestorePointManager.cs:32-44` devuelve un booleano y registra un supuesto número de secuencia obtenido de la respuesta; su disponibilidad real debe comprobarse con el contrato WMI y en Windows.

**Impacto:** posible falsa sensación de recuperación; restaurar valores prefijados puede cambiar una configuración preexistente y no revertirla; pérdida silenciosa de historial.

**Cambio:** preflight central previo a cada modificación aplicable; comprobar estado y creación real del punto, identificador y fecha mediante API soportada, e informar si Windows no puede crearlo. Ante fallo, detener y solicitar autorización específica para continuar sin él; para operaciones de alto riesgo exigir medidas adicionales o bloquear. Capturar existencia/tipo/datos del registro, estado de servicios, red y plan de energía antes de modificar; compensar en orden inverso si es viable y reportar pasos no reversibles. Historial transaccional y error de persistencia visible. No restaurar valores por defecto como si fueran anteriores.

### P1-03 — Acciones modificadoras etiquetadas como «solo lectura»

**Evidencia:** `ModuleAction` usa `changesSystem=false` por defecto (`UI/ModuleAction.cs:16-21`); `SoftwareModule.cs:15-22` instala paquetes y `TweaksModule.cs:14-21` aplica/revierte ajustes sin establecerlo. `Views/ActionCardViewModel.cs:29-33` usa ese campo para mostrar «Solo lectura». Estas operaciones tienen confirmaciones internas, pero la clasificación y protección central son incorrectas.

**Impacto:** información engañosa y omisión de cualquier política futura basada en ese campo.

**Cambio:** eliminar el valor por defecto y exigir metadatos tipados: efecto, alcance, riesgo, privilegio, precondiciones, reversibilidad y política de cancelación. Auditar todas las acciones, añadir prueba que prohíba efectos no declarados y confirmar la acción concreta tras recopilar sus parámetros.

### P1-04 — No existe un modelo de diagnóstico previo ni informe final

**Evidencia:** las acciones SFC/DISM se ofrecen directamente (`RepairModule.cs:15-32`); `ModuleAction.cs:16-34` solo describe una acción ejecutable; `ModulePage.xaml.cs:23-27` presenta tarjetas; el panel permite copiar salida (`ShellWindow.xaml.cs:455-462`) pero no construye informe estructurado. No se observan `CheckHealth`/`ScanHealth`, diagnóstico BCD ni estado Defender en el catálogo revisado.

**Impacto:** el usuario no conoce qué problema justifica una reparación ni dispone de estados completos para soporte. SFC `/scannow` es reparador, no una comprobación de solo lectura.

**Cambio:** definir `CheckDefinition`, `CheckResult`, `RepairDefinition` y `OperationReport`; asociar reparación a evidencia reciente y precondiciones. Diagnósticos bajo demanda para imagen, disco, arranque, red, drivers, registro, rendimiento, seguridad y eventos, con `No disponible/Permiso requerido` explícito. Mantener utilidades técnicas independientes como categoría avanzada identificada, sin presentarlas como reparación diagnosticada. Exportar resumen legible + detalles y salida, con política de privacidad.

### P1-05 — Interpretación frágil y riesgos de entrada

**Evidencia:** `RepairModule.cs:108-132` interpreta frases en inglés y prioriza `has been scanned` como «sin errores»; la coincidencia no prueba ausencia de errores y la salida de Windows puede estar localizada. `NetworkModule.cs:64-67` y `AdvancedNetworkModule.cs:96-99` aceptan cualquier `IPAddress`, aunque los comandos usados son de interfaz IPv4; máscaras no se validan como prefijos contiguos. Adaptadores, proxy, impresoras, filtros y patrones se interpolan en cadenas (`NetworkModule.cs:46-59`, `AdvancedNetworkModule.cs:33-76`, `DevicesModule.cs:40-56`, `RecoveryModule.cs:27-46`, `SoftwareModule.cs:98-106`).

**Impacto:** falsos diagnósticos, argumentos inesperados o acciones sobre un destino incorrecto; no se afirma una explotación concreta sin prueba en Windows.

**Cambio:** parsers conservadores por herramienta/versiones e idiomas o APIs estructuradas; cuando falte certeza mostrar «resultado no interpretado», nunca sano. Validar familias IPv4/IPv6 según comando, máscara y pasarela, seleccionar adaptador por identidad estable y revisar argumentos por esquema. Para Appx, enumerar coincidencias y confirmar el conjunto exacto; para recuperación validar unidades reales, destino independiente y sintaxis de `winfr` instalada.

### P1-06 — Registro y privacidad

**Evidencia:** `Core/AppLogger.cs:19-55` crea texto libre sin rotación ni límites y almacena salida completa; `Core/AppHost.cs:28-32,40-43` registra rutas locales; `CommandExecutor.cs:116-153,161-171` no usa el mismo registro completo para cancelación, timeout o error de inicio. `README.md` advierte que la salida puede contener datos personales.

**Impacto:** exposición de rutas, identificadores, eventos o datos de red y trazabilidad incompleta de fallos. En medios de solo lectura, la creación obligatoria de `logs/` puede impedir el arranque pese al respaldo de configuración incrustada (`AppLogger.cs:21-23`; `ConfigManager.cs:92-104`).

**Cambio:** eventos estructurados con ID de operación, fecha UTC, versión, SO/arquitectura, elevación, duración, comando/argumentos sanitizados y resultado también en cancelación/fallo; limitar tamaño/retención, proteger ACL y separar salida detallada opcional. Exportación con vista previa y redacción por defecto; advertir que la salida completa puede contener datos sensibles. Manejar de forma explícita ausencia de almacenamiento escribible sin afirmar que la auditoría se guardó.

### P2-01 — Accesibilidad, interacción y presentación pendientes

**Evidencia:** `App.xaml:17-35` define colores propios solo para Light/Dark; `ActionCardViewModel.cs:12-23` y `ShellWindow.xaml.cs:73-79` usan colores fijos. `ActionCard.xaml:28-34,46-48` limita líneas y fija altura; `DialogService.cs:28-32,78-82` fija anchos; el panel ocupa altura fija (`ShellWindow.xaml.cs:432-436`). No se encontraron implementaciones de `KeyboardAccelerator`, anuncios `AutomationProperties.LiveSetting` ni notificaciones/toast en el código revisado. El autoscroll siempre va al final (`ShellWindow.xaml.cs:303-310`).

**Impacto:** posibles problemas de contraste, truncamiento, seguimiento con Narrator y pérdida de posición de lectura. La conformidad visual real debe verificarse en Windows, no deducirse solo del XAML.

**Cambio:** recursos de sistema/alto contraste y acento; diseño adaptable y controles de tamaño suficiente; F5, Ctrl+R, Ctrl+L, Esc y F1 con contexto y sin conflictos; nombres accesibles y anuncios de cambios; autoscroll solo al estar al final; respetar animaciones reducidas. Probar DPI 100/150/200 %, escalado de texto, tema y alto contraste, teclado y Narrator. Bandeja y toast son mejoras opcionales de la skill, subordinadas a una operación segura en segundo plano; no son sustituto del informe.

### P2-02 — CI y documentación no verifican las promesas

**Evidencia:** no hay proyectos de pruebas en el repositorio; el workflow `.github/workflows/portable-windows.yml:23-89` compila/publica y verifica archivo/manifiesto, no ejecuta pruebas funcionales. `ci-check-app-start.ps1:8-16,23-28` apunta a `net8.0` y a `WDigger.exe.crashLog`, mientras el proyecto usa `net10.0` (`WDigger.csproj:5`) y la app indica `WDigger.crash.log` (`App.xaml.cs:67`); el script no aparece invocado en el workflow. El README pide .NET 8 o posterior pese al target .NET 10, duplica notas de versión 3.0 y el ensamblado declara `2.0.0` (`WDigger.csproj:19-23`).

**Impacto:** se puede publicar sin detectar errores de inicio, seguridad o regresiones; instrucciones y versiones confusas.

**Cambio:** CI Windows con SDK fijado, compilación y tests obligatorios, publicación, inspección correcta del manifiesto y smoke test controlado con timeout; actualizar o retirar el script obsoleto. Unificar versiones y requisitos, describir cobertura real, privilegios, modo offline, límites de recuperación y simulación.

## 4. Matriz por módulo y cobertura de las skills

| Área / módulo | Estado observado | Cambio prioritario |
|---|---|---|
| Dashboard y hardware | Inventario WMI, espacio y sensores; sin estado de salud global tipado. | Diferenciar inventario de diagnóstico; disponibilidad/errores; accesibilidad y permisos. |
| Reparación | SFC/DISM reparadores y CHKDSK; reset de Update con múltiples pasos. | P0 resultados/cancelación; CheckHealth/ScanHealth, preflight y acciones asociadas a hallazgos. |
| Software | Instalación por JSON y eliminación Appx; instalaciones mal etiquetadas. | Clasificación correcta, esquema de paquetes seguro, listado exacto y confirmación. |
| Ajustes y privacidad | Configuración externa y rollback predeterminado. | P0 eliminar comandos arbitrarios; guardar estado real y resultados parciales. |
| Controladores | Enumeración, exportación e instalación INF. | Corregir simulación; validar origen/firmas, respaldo, punto de restauración y consecuencias. |
| Red / red avanzada | DNS, DHCP, Winsock, TCP/IP, proxy, nombre. | Avisar desconexión/sesión remota y reinicio; validar parámetros, capturar estado y compensar. |
| Dispositivos | WMI, impresoras y habilitación PnP. | Identidad estable, confirmar dispositivo concreto, advertir periféricos esenciales, respaldo si aplica. |
| Copias Acronis | Localización y restauración hacia unidad seleccionada. | P0 deshabilitar restauración hasta validar herramienta y confirmación final del destino. |
| Activación | Consulta/activación por scripts locales; detección HEU-KMS. | Separar consulta/cambio, validar ejecutables/rutas y presentar limitaciones; no ejecutar herramientas de terceros. |
| Recuperación | Lanza `winfr` con origen, destino, modo y filtro. | Validar volúmenes físicos y versión/sintaxis; nunca escribir en origen; confirmar consecuencias. |
| Eventos y BIOS | Consultas de eventos y WMI. | Filtrar eventos críticos por ventana temporal, tratar permisos y evitar confundir error WMI con resultado válido. |
| Discos y limpieza | Espacio, CHKDSK y borrado de temporales. | Previsualizar borrado y alcance, exclusiones y resultado; no afirmar reversibilidad; comprobar capacidad y permisos de CHKDSK. |

**Trazabilidad por skill:**

- **Core:** ejecución y privilegios P0-02/P0-04/P1-01; restauración y reversión P1-02; catálogo P1-04; auditoría P1-06.
- **UX:** diagnóstico previo P1-04; consecuencias y confirmación final P0-03/P1-03; restauración P1-02; informe P1-04/P1-06; UAC P1-01.
- **UI:** estados y detalles P0-01/P1-04; controles destructivos P0-03; progreso y tema/accesibilidad P0-04/P2-01.
- **IX:** cancelación P0-04; progreso/log P1-06/P2-01; teclado, Narrator, animaciones y bandeja opcional P2-01; rechazo UAC P1-01.

## 5. Plan de cambios y criterios de aceptación

Las fases son secuenciales en sus dependencias; UI y pruebas pueden avanzar en paralelo sobre contratos definidos. Estimaciones orientativas de una persona con experiencia en .NET/WinUI y acceso a VMs, **no compromisos**; confirmar tras prototipo, decisión sobre el portable y versiones de Windows. Las pruebas en paralelo y la disponibilidad de infraestructura pueden cambiar la duración.

### Fase 0 — Contención (2–4 días)

- Bloquear restauración Acronis y ajustes que acepten comandos arbitrarios; bloquear reparación de Windows Update hasta proteger su secuencia.
- Corregir las clasificaciones de instalación/ajuste; impedir cambiar simulación durante una ejecución y eliminar escrituras externas en modo simulado.
- Propagar errores y estados parciales; eliminar mensajes de éxito no verificados. Revisar confirmaciones para Appx, red, dispositivo y limpieza.
- **Aceptación:** pruebas con ejecutor falso: cada fallo inyectado impide éxito e historial falso; dry-run no modifica el destino; las acciones bloqueadas no lanzan procesos; no se ejecutan operaciones destructivas en CI.

### Fase 1 — Fundamentos seguros (1–2 semanas)

- Separar dominio del shell; introducir contratos de operación, resultado, privilegio, riesgo, cancelación y compensación.
- Rediseñar ejecutor para argumentos tipados, captura concurrente, timeout real, cancelación por política, codificación y salidas acotadas; eliminar shell cuando sea posible.
- Registrar eventos estructurados y guardar estados previos/historial de forma recuperable; política de punto de restauración, fallo y excepciones documentadas.
- **Aceptación:** tests de stdout silencioso/masivo, stderr, timeout, árbol de procesos, rutas con espacios/comillas, cancelación y códigos no cero; restauración fallida detiene o exige confirmación específica; reversión restaura también valores que antes no existían.

### Fase 2 — Mínimo privilegio (1–2 semanas, depende de fase 1)

- Diseñar y prototipar auxiliar elevado con IPC protegido, allowlist de operaciones y validación independiente. Resolver despliegue seguro portable/único EXE y ubicación de datos escribibles.
- Cambiar aplicación a `asInvoker`; clasificar comprobaciones según permisos reales y comportamiento en Windows; explicar UAC y gestionar rechazo/política.
- **Aceptación:** arranque y pruebas disponibles sin UAC; no es posible solicitar comandos arbitrarios o manipular parámetros fuera de esquema al auxiliar; rechazo de UAC mantiene UI utilizable; pruebas de integridad, ACL y manipulación del canal en VM. CI valida el manifiesto nuevo y el empaquetado acordado.

### Fase 3 — Diagnóstico e informe (1–2 semanas, depende de fases 1–2)

- Implementar catálogo de comprobaciones con resultado estructurado, severidad, evidencia, fecha, privilegio y acción recomendada. Introducir comprobaciones de solo lectura compatibles y declarar las que requieren permisos.
- Reparación únicamente tras diagnóstico asociado, preflight y confirmación final; excepciones técnicas avanzadas explícitas. Incorporar informe exportable con resumen y detalles controlados por privacidad.
- **Aceptación:** escenario limpio, advertencia, error, no disponible y permisos denegados; fallo/éxito parcial/cancelación/reinicio/manual reflejados en UI e informe; `Simulado` nunca figura como reparado; ninguna acción reparadora se ofrece como diagnóstico de solo lectura.

### Fase 4 — UX, UI e IX (1–2 semanas, sobre fase 3)

- Rediseñar resultados por severidad e impacto, detalles expandibles, acciones seleccionadas y confirmaciones con consecuencias y checkbox para alto riesgo.
- Implementar progreso por operación y pasos, atajos, foco, Narrator, alto contraste, DPI y animación reducida; corregir autoscroll. Evaluar bandeja/toasts solo tras definir ciclo de vida seguro.
- **Aceptación:** recorrer flujo completo con teclado y Narrator; verificar 100–200 % DPI, texto ampliado, claro/oscuro y alto contraste; cancelar solo cuando corresponda y conservar posición del log.

### Fase 5 — Validación y publicación (3–5 días, continua desde fase 0)

- Automatizar tests unitarios, contrato de ejecutor, preflight, reversión, clasificación y serialización; agregar CI y smoke test del artefacto. Mantener tests peligrosos exclusivamente en VMs descartables con snapshots.
- Matriz mínima: Windows 10 y 11 dentro de las versiones efectivamente soportadas por las dependencias; usuario estándar/admin; UAC aceptado/rechazado; System Restore activo/desactivado; entorno sin red y medio de solo lectura; Windows en español/inglés; fallos, cancelación y reinicio.
- Validar sintaxis y disponibilidad de Acronis, `winfr`, Winget/Chocolatey y fuentes de reparación para las versiones soportadas. Revisar integridad, licencias y versiones de dependencias antes de fijar la matriz.
- **Aceptación:** CI bloquea regresiones; artefacto arranca y registra correctamente en Windows; ninguna prueba automática toca un sistema real; publicación condicionada al cierre de P0 y a evidencias de los escenarios P1 acordados.

## 6. Decisiones que requieren aprobación

1. **Distribución:** ¿mantener un único EXE es obligatorio si el auxiliar aislado requiere extracción protegida, o se acepta un paquete portable con varios archivos firmados?
2. **Operaciones peligrosas:** ¿deshabilitar de forma permanente restauración de imágenes y comandos configurables, o conservarlos como funciones avanzadas tras verificación específica?
3. **Modo offline y datos:** ¿dónde se guardan logs y snapshots si el USB es de solo lectura? ¿Se permite continuar sin log persistente, y para qué operaciones?
4. **Compatibilidad:** versiones concretas de Windows, herramientas de terceros y condiciones para reparaciones en equipos remotos.
5. **Privacidad:** retención, datos ocultos por defecto, consentimiento de exportación de salidas completas y protección de informes.

## 7. Resultado de la auditoría

Se entrega este documento como propuesta. No se han aplicado cambios a la aplicación, no se han lanzado comandos de reparación ni se han verificado comportamientos dinámicos en Windows. La siguiente etapa recomendada es aprobar las decisiones de la sección 6 y ejecutar primero la fase de contención.
