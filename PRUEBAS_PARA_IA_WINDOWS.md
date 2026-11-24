# Protocolo de pruebas de WDigger para una IA asistente en Windows

## Instrucciones para la IA que recibe este documento

Tu función es acompañar al propietario, no decidir por él ni ejecutar cambios sin permiso. Trabaja paso a paso, explica cada acción y espera el resultado. No afirmes que algo funciona por haber compilado o por ver un mensaje de éxito. Registra los fallos y detente si observas un comportamiento inseguro. Este documento describe el código de la rama de trabajo, no certifica un ejecutable ya existente.

**Seguridad:** comienza con lectura y simulación. No ejecutes reparaciones reales, desinstalaciones, ajustes, limpiezas, cambios de red o de servicios en el equipo principal. Pruebas modificadoras exclusivamente en una VM Windows desechable con snapshot y autorización expresa por acción. No desactives antivirus, UAC, firewall ni políticas de seguridad para hacer pasar una prueba. No solicites contraseñas, claves, tokens, volcados de memoria ni datos privados. Los informes y logs pueden contener nombres de usuario, rutas, IP y software instalado: pide revisión y redacción antes de compartirlos.

**Estado conocido:** la aplicación sigue en desarrollo, no está validada para producción. Es WinUI 3 autocontenida para Windows 10/11 x64 (mínimo configurado: Windows 10 build 17763). XP, Vista, 7, 8 y 8.1 no están soportados. La instalación y actualización con WinGet/Chocolatey no están implementadas; solo se comprueban rutas habituales, sin ejecutar ni verificar gestores. Algunas acciones existentes solo funcionan en simulación. No conviertas ninguna de estas limitaciones en una prueba fallida de funcionalidad prometida.

## 1. Identificación y preparación

1. Pregunta al usuario si el equipo es de producción o una VM y si tiene autorización para usarlo. Confirma que no haya trabajos críticos abiertos y que existe copia de seguridad. Para pruebas destructivas, exige VM con snapshot restaurable. No uses el equipo principal para la sección 6.
2. Obtén la **misma revisión de los archivos fuente con los cambios de esta conversación**, incluida la carpeta de archivos no versionados si se transfiere manualmente. Pregunta cómo los ha recibido el usuario; no hagas `git reset`, `clean`, `checkout`, ni borres cambios locales. Si se usa Git, anota `git branch --show-current`, `git rev-parse HEAD` y `git status --short`. Un commit base no identifica por sí solo cambios sin commit.
3. Registra edición, build, arquitectura, idioma de Windows y tipo de entorno. Se pueden consultar sin cambios con PowerShell:

   ```powershell
   Get-CimInstance Win32_OperatingSystem | Select-Object Caption, Version, BuildNumber, OSArchitecture, OSLanguage
   [Environment]::Is64BitOperatingSystem
   ```

4. Comprueba espacio libre, conexión y que el usuario puede aceptar UAC. No ejecutes archivos desconocidos. Registra el origen del binario y su hash SHA-256. **No uses `WDigger.publish\WDigger.exe` para evaluar los cambios actuales**: es un binario incluido previamente en el repositorio y puede estar desactualizado.
5. La ejecución portable no requiere instalar un runtime aparte, pero **compilar** requiere el SDK .NET 10 y descarga de paquetes NuGet. Consulta `dotnet --info`; si no está instalado, pregunta al usuario antes de instalar nada. Como alternativa, usa un artefacto de CI construido a partir de esta revisión exacta y verifica su procedencia y hash. Si no hay fuente o artefacto verificable, detente y solicita uno; no declares aprobada la versión.

## 2. Compilación y pruebas automatizadas (solo con fuente y SDK disponibles)

Desde la raíz de la copia exacta, en PowerShell, ejecuta cada comando por separado y comprueba `$LASTEXITCODE` antes de pasar al siguiente:

```powershell
dotnet run --project tests\WDigger.SafetyTests\WDigger.SafetyTests.csproj -c Release
dotnet build src\WDigger\WDigger.csproj -c Release
dotnet publish src\WDigger\WDigger.csproj -c Release -p:PublishProfile=Portable-win-x64
```

El SDK restaura paquetes por red. Las pruebas usan archivos temporales y claves de prueba aisladas bajo HKCU que intentan limpiar al terminar. Documenta cualquier resto o error; no elimines manualmente claves o archivos sin inspeccionarlos y sin permiso. No pases al siguiente paso si hay errores. Guarda el texto de error y versión de SDK sin secretos. Si el entorno no permite instalar el SDK, indica `NO EJECUTADA`, no `APROBADA`.

La ruta prevista del artefacto es `artifacts\portable\WDigger.exe`; comprueba que existe y anota:

```powershell
Get-Item .\artifacts\portable\WDigger.exe | Select-Object FullName, Length, LastWriteTimeUtc
Get-FileHash .\artifacts\portable\WDigger.exe -Algorithm SHA256
```

Un build exitoso no prueba portabilidad, compatibilidad ni seguridad. No distribuyas el ejecutable como versión validada hasta terminar la matriz pertinente.

## 3. Arranque y portabilidad, sin reparaciones

1. En una carpeta de prueba escribible, copia únicamente el EXE recién publicado. Comprueba el hash de la copia; no cambies el original. Desconecta la red **solo si el usuario lo autoriza y no interrumpe una sesión remota**. Abre el EXE y acepta UAC. Debe aparecer la interfaz con `ADMINISTRADOR`. Informa si falla o solicita instalar runtime; registra también si Windows extrae componentes temporales. Reconecta la red si se desconectó.
2. Cierra normalmente. Vuelve a abrir y rechaza UAC: no debe aparecer la interfaz ni iniciarse una operación. No intentes evitar UAC.
3. Abre con `--dry-run` y verifica que el interruptor y el aviso de simulación estén activos. La simulación evita acciones modificadoras de WDigger, pero **no es una sandbox**: las consultas, el registro y las exportaciones todavía pueden leer o escribir datos. No desactives simulación durante la sección 4.
4. Si existe un medio de prueba de solo lectura, comprueba únicamente el arranque y las ubicaciones mostradas en el panel. Debe usar la ruta alternativa en `%LOCALAPPDATA%\WDigger` cuando no pueda escribir junto al EXE. No modifiques permisos del equipo principal para forzar el caso. Anota rutas y errores sin publicarlos sin revisión.
5. Opcionalmente, en VM y con autorización, `ci-check-app-start.ps1 -Executable <ruta-del-exe>` comprueba solo que el proceso sigue vivo durante unos segundos y no crea crash log nuevo; el script termina ese proceso de forma forzada. No lo uses con operaciones en curso. Un PASS no reemplaza las pruebas funcionales.

## 4. Matriz funcional no destructiva

Anota para cada prueba: ID, fecha UTC, build y hash, resultado (`APROBADA`, `FALLIDA`, `BLOQUEADA` o `NO EJECUTADA`), pasos, resultado observado y evidencia revisada. Las operaciones de lectura pueden incluir consultas WMI/PowerShell y generar logs; la exportación crea un archivo elegido por el usuario.

| ID | Prueba | Resultado esperado |
|---|---|---|
| F01 | Navega por las categorías y vuelve al panel; prueba temas y tamaño de ventana. | No hay crash; acciones, descripciones y salida son legibles. |
| F02 | Busca globalmente `reparacion`, `disco`, `privacidad` y dos palabras como `reparar disco`. Selecciona un resultado. | Ignora mayúsculas y acentos, requiere todas las palabras, abre la categoría y filtra por la acción elegida. No ejecuta la acción al seleccionar el resultado. |
| F03 | Filtra dentro de una categoría con texto existente, inexistente y vacío. | Cambian tarjetas y contador; no se ejecuta ninguna acción. |
| F04 | Software → inventario registrado. | Lista nombre, versión, editor y ámbito; advierte si alguna rama no pudo leerse. No instala ni desinstala nada. Puede omitir software portable y Appx; el usuario es el de la sesión elevada. |
| F05 | Software → exportar inventario. Guarda en una carpeta de prueba y abre el TXT. | El informe incluye aviso de privacidad, entradas y posibles advertencias. Cancelar el selector no debe crear archivo. Comprueba que el contenido no incluya líneas inventadas por caracteres de control. Revísalo antes de compartir. |
| F06 | Software → ver Appx removibles. | Se muestran solo los paquetes removibles del usuario elevado actual, no otros usuarios ni paquetes provisionados; si la consulta falla, se informa el error. No se elimina nada. |
| F07 | Software → comprobar gestores. | Solo se muestran candidatos de WinGet y Chocolatey en rutas habituales como **sin verificar** o no detectados en esa ruta. No se lanzan ni instalan gestores. Su ausencia en esa ruta no demuestra ausencia total. |
| F08 | Ajustes y privacidad → consultar ajustes y copias. | La tabla muestra valor registrado y estado de copia para los tres ajustes fijos. No cambia el registro ni afirma que la directiva esté activa. |
| F09 | Diagnóstico → analizar y, si se autoriza, exportar a carpeta de prueba. | Los resultados no disponibles o errores son explícitos; se puede revisar el informe y no se inicia reparación automáticamente. |
| F10 | Con simulación activa, elige una acción modificadora de bajo impacto y cancela cualquier diálogo de selección. | No hay cambio real; la UI no presenta simulación como reparación exitosa. Si se cancela antes del inicio, debe indicarlo. |
| F11 | Con teclado y Narrador, navega búsqueda, resultados y botones; prueba 100 %, 150 %, 200 % DPI y alto contraste si es viable. | Elementos identificables, enfoque visible y sin contenido esencial ilegible. Documenta limitaciones en vez de suponer accesibilidad. |
| F12 | Comprueba una acción marcada como no disponible, si la versión expone alguna, pulsando tanto el botón como el fondo de la tarjeta. | No se ejecuta. Las acciones pendientes de revisión deben quedar bloqueadas en modo real. |

No uses `Quitar un paquete Appx`, `Aplicar un ajuste`, `Revertir`, SFC, DISM /RestoreHealth, CHKDSK, Windows Update reset, limpieza real ni activación en el equipo principal durante estas pruebas. Algunas acciones no disponibles pueden no estar presentes: marca F12 como no ejecutada si no existe una adecuada.

## 5. Evidencia de WinGet y Chocolatey, sin ejecución

La IA debe preguntar si el usuario desea recopilar evidencia adicional. La detección de WDigger no ejecuta gestores ni demuestra que un archivo sea legítimo. Si se desea, inspecciona **manualmente y en solo lectura** las propiedades y firma digital de cada candidato detectado; no confíes en `PATH` ni ejecutes archivos hallados en carpetas escribibles por usuarios. El alias de WinGet puede no ser un ejecutable normal o no funcionar. Chocolatey puede estar instalado en otra ubicación. Apunta ubicación, tipo de archivo, estado de firma y versión solo si pueden obtenerse sin ejecutar el candidato; redacta rutas personales antes de compartir. No instales gestores ni aceptes licencias automáticamente. No pruebes instalación, actualización o desinstalación porque esa integración aún no existe.

## 6. Pruebas de cambios reales: solo VM con snapshot

Estas pruebas requieren aprobación expresa individual, copia de seguridad y capacidad de restaurar la VM. Usa una instalación descartable, no datos reales ni una sesión remota que pudiera perderse. Consulta el protocolo detallado `VALIDACION_VM_WINDOWS.md` para UAC, journal, restauración, Appx, SFC/DISM/CHKDSK, red y Windows Update. No hagas todas las operaciones en cadena: prepara snapshot independiente por caso, revisa el riesgo mostrado y observa los resultados antes de la siguiente.

Antes de aceptar cualquier cambio, comprueba la confirmación de riesgo y recuperación. Rechazarla debe impedir la acción. Si falla la creación/verificación del punto de restauración, rechaza continuar en la primera prueba. Un punto de restauración no sustituye un backup ni garantiza reversión. Para probar un ajuste concreto usa solo la VM, registra su valor previo, aplica y revierte mediante WDigger, y verifica el resultado y journal. No alteres manualmente valores ni fuerces interrupciones sin un caso específico y aprobación. No infieras salud del sistema solo de un código de salida cero.

## 7. Informe de regreso

Entrega al propietario un resumen con: revisión/estado de fuente, hash del EXE, Windows/build/arquitectura/idioma, SDK si aplica, entorno físico o VM, conectividad, resultados por ID, pasos exactos de fallos, texto de error sanitizado y pruebas pendientes. Para cada fallo indica si es reproducible y si hubo algún cambio real. Distingue claramente observación, hipótesis y no probado. Nunca adjuntes logs, informes de software, capturas o journal sin que el usuario los revise y autorice. Si hay fallo de build incluye el primer error completo y contexto sin rutas privadas. No recomiendes usar WDigger en producción basándote únicamente en este protocolo.
