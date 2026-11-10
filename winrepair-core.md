---
name: winrepair-core
description: >-
  Skill de dominio para el motor de diagnóstico y reparación de una herramienta
  nativa de Windows (.NET / C++ / PowerShell embebido). Úsala al implementar o
  auditar comprobaciones del sistema, ejecución segura de comandos, elevación,
  puntos de restauración, rollback y logging. Cubre SFC, DISM, CHKDSK, WMI,
  registro, drivers y las reglas de seguridad para no romper el sistema.
---

# Motor de Diagnóstico y Reparación Windows: Comprobaciones, Seguridad y Rollback

Esta skill define las reglas del **núcleo técnico** de una herramienta de reparación de Windows. Una herramienta que diagnostica mal o repara de forma agresiva puede dejar el sistema peor que antes. La regla de oro: **primero no dañar**.

## 1. Catálogo mínimo de comprobaciones

| Categoría | Comprobación | Comando / API | Solo lectura |
|---|---|---|---|
| **Integridad del sistema** | Archivos de sistema dañados | `sfc /scannow` | No |
| **Imagen de Windows** | Salud de la imagen | `DISM /Online /Cleanup-Image /CheckHealth` y `/ScanHealth` | Sí (Check/Scan), No (RestoreHealth) |
| **Disco** | Sectores defectuosos, errores de sistema de archivos | `chkdsk /scan` (solo lectura) y `chkdsk /f` (reparación) | Mixto |
| **Arranque** | Estado del arranque, BCD | `bcdedit /enum`, `bootrec /scanos` | Sí (enum), No (reparación) |
| **Red** | Conectividad, DNS, adaptadores, Winsock | `ping`, `nslookup`, `ipconfig /all`, `netsh winsock show catalog` | Sí (show), No (reset) |
| **Drivers** | Drivers con errores, versiones | WMI `Win32_PnPEntity`, `Get-WindowsDriver` | Sí |
| **Registro** | Claves de arranque, servicios huérfanos | WMI / acceso al registro | Mixto |
| **Rendimiento** | Uso de CPU/RAM/disco, servicios innecesarios | WMI `Win32_PerfFormattedData_*` | Sí |
| **Seguridad** | Estado de Defender, firewall, actualizaciones | `Get-MpComputerStatus`, `netsh advfirewall show allprofiles` | Sí |
| **Eventos** | Errores críticos recientes | `Get-WinEvent` con filtros | Sí |

**Regla:** toda comprobación de solo lectura se ejecuta **sin elevación**. Solo las reparaciones que modifican el sistema piden UAC.

## 2. Ejecución segura de comandos

- **Nunca construyas comandos por concatenación de strings.** Usa `ProcessStartInfo` con `ArgumentList` (no `Arguments`) para evitar inyección.
- Ejecuta comandos con **timeout** y **captura de stdout/stderr** por separado. Un `sfc /scannow` puede tardar 30 minutos: el timeout debe ser configurable y el proceso cancelable.
- Registra **siempre** el comando exacto, su salida y su código de retorno en el log. Es la base del soporte y de la auditoría.
- Para PowerShell embebido, usa `-NoProfile -NonInteractive -ExecutionPolicy Bypass` y un script firmado o embebido como recurso, no un script descargado en tiempo de ejecución.

## 3. Puntos de restauración y rollback

- Antes de la primera modificación del sistema, **crea un punto de restauración** con `Checkpoint-Computer` (PowerShell) o `SRSetRestorePoint` (API). Si la creación falla, informa al usuario y pide confirmación explícita para continuar sin punto de restauración.
- Para reparaciones que lo permitan, guarda el **estado previo** (valor del registro, configuración de red, lista de drivers) para poder revertir.
- Define un **plan de rollback** por reparación: si la reparación falla a medias, ejecuta las acciones de reversión registradas.
- El punto de restauración debe quedar registrado en el informe con su identificador y fecha.

## 4. Elevación y mínimo privilegio

- La herramienta corre **sin elevar** por defecto. Solo eleva un **proceso auxiliar** para las operaciones que lo requieren, no toda la aplicación.
- Usa `runas` o el manifiesto `requestedExecutionLevel` con `asInvoker` en el proceso principal y un helper elevado bajo demanda.
- El helper elevado acepta **solo los comandos predefinidos** por la herramienta, nunca comandos arbitrarios del proceso principal. Esto evita que una vulnerabilidad en la UI se convierta en ejecución arbitraria con privilegios.

## 5. Logging y auditoría

- Log estructurado (JSON lines o similar) con: timestamp, severidad, categoría, comando, salida, código de retorno, duración y si hubo elevación.
- Rotación de logs y límite de tamaño. Los logs no deben contener rutas de usuario completas ni datos personales más allá de lo necesario.
- El log es **exportable** para soporte. Incluye un encabezado con versión de la herramienta, versión de Windows, arquitectura y estado de elevación.

## 6. Anti-patrones que la herramienta debe evitar

| Anti-patrón | Corrección |
|---|---|
| Ejecutar `sfc /scannow` en cada arranque de la app | Diagnóstico bajo demanda o programado, nunca en cada apertura |
| `chkdsk /f` sin advertir del reinicio | Advertir que requiere reinicio y puede tardar horas |
| Modificar el registro sin backup | Exportar la clave antes de modificarla y registrar la ruta del backup |
| Desinstalar drivers sin punto de restauración | Crear punto de restauración y ofrecer rollback |
| Resetear Winsock sin avisar de la desconexión | Confirmación explícita con consecuencias (ver `winrepair-ux`) |
| Ejecutar comandos arbitrarios desde la UI | Helper elevado con lista blanca de comandos |
| Ocultar el código de retorno al usuario | Mostrarlo en detalles técnicos y traducirlo en la cara principal |

## 7. Checklist del motor

- [ ] ¿Las comprobaciones de solo lectura se ejecutan sin elevación?
- [ ] ¿Los comandos se construyen con `ArgumentList` y tienen timeout y cancelación?
- [ ] ¿Se registra comando, salida y código de retorno en el log estructurado?
- [ ] ¿Se crea un punto de restauración antes de la primera modificación y se registra en el informe?
- [ ] ¿Cada reparación tiene un plan de rollback definido?
- [ ] ¿El proceso elevado acepta solo comandos de una lista blanca?
- [ ] ¿El log es exportable y no contiene datos personales innecesarios?
- [ ] ¿Se evitan los anti-patrones de la sección 6?