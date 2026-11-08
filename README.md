# WDigger

WDigger es una herramienta **nativa de Windows** para diagnóstico y reparación,
dirigida a técnicos de PC y portátiles. Es una alternativa *offline-first* a
herramientas como WinUtil, con inventario WMI, sensores en tiempo real, estado
SMART, batería y utilidades de mantenimiento.

La interfaz es una aplicación **WinUI 3 (Windows App SDK)** con Fluent Design:
barra de título personalizada, navegación lateral (`NavigationView`) con los 13
módulos además del panel de control, tarjetas de acción, tema claro/oscuro/
automático persistente, y un **panel de salida integrado** con la salida en
vivo de los comandos (sin consolas emergentes), resultados en tabla, progreso y
botón de cancelación.

> **Herramienta de uso personal.** No está destinada a distribución comercial.
> WDigger solicita siempre elevación mediante UAC y no continúa sin privilegios
> de administrador.

## Características de la aplicación

- **Fluent Design nativo:** controles WinUI 3, fuente Segoe UI Variable, Mica,
  DPI por monitor y compatibilidad con Windows 10/11.
- **Panel de control:** resumen del sistema (SO, CPU, memoria, disco C:),
  acciones rápidas, ubicaciones de datos y registro de auditoría.
- **Tema claro/oscuro/automático:** se cicla desde el pie de navegación y se
  guarda en `config/ui-settings.json`.
- **Panel de salida integrado:** stdout/stderr en vivo con colores, copia al
  portapapeles, limpieza, vista de tabla y botón de cancelación.
- **Tarjetas de acción:** cada operación muestra descripción, insignia
  «Modifica el sistema» / «Solo lectura» y botón Ejecutar.
- **Portable:** un único `WDigger.exe` x64 con .NET, Windows App SDK
  (self-contained) y las dependencias incluidas. Sin instalador ni runtime previo.
- **Siempre administrador:** `app.manifest` declara `requireAdministrator` y el
  arranque vuelve a comprobar la elevación como medida adicional.
- **Sin consolas emergentes:** DISM, SFC, CHKDSK, PowerShell, Winget, netsh y el
  resto de procesos muestran su salida dentro de la ventana principal.
- **Operaciones cancelables:** las tareas largas se ejecutan de forma asíncrona;
  al cancelar, WDigger intenta finalizar también el proceso y sus descendientes.
- **Datos junto al ejecutable:** `config/`, `logs/`, el historial de ajustes y
  los archivos de errores se guardan en la carpeta portable.
- **Configuración autocontenida:** los JSON originales están incrustados en el
  EXE. Si no existen archivos externos, WDigger usa esos valores e intenta crear
  copias editables en `config/`. Acepta el formato con envoltura (`{"tweaks": [...]}`,
  `{"packages": [...]}`) y también arrays JSON sueltos. En un USB de solo lectura
  puede seguir usando los valores incrustados.
- **Modo simulación:** interruptor «Simulación» en la barra de título (también
  `--dry-run`) registra las acciones de modificación sin ejecutarlas; persiste;
  se destaca con un aviso en el panel de control.

## Stack

| Capa | Tecnología |
|---|---|
| Lenguaje / runtime | C# 12, .NET 8, Windows x64 |
| Interfaz | WinUI 3 (Windows App SDK 1.8, self-contained), Fluent Design |
| Sensores | LibreHardwareMonitorLib |
| Inventario / SMART / batería / BIOS | System.Management (WMI) |
| Comandos | DISM, SFC, CHKDSK, netsh, PowerShell, Winget, pnputil, winfr, wevtutil, slmgr y ospp.vbs |
| Configuración | JSON editable en `config/`, con valores incrustados de respaldo |
| Auditoría | Registro por sesión en `logs/`, incluyendo stdout, stderr, código y duración |
| CI | GitHub Actions sobre `windows-latest` publica y verifica el EXE portable |

## Estructura

```text
src/WDigger/
├─ App.xaml(.cs)              # Recursos de tema + arranque (elevación UAC y crash logs)
├─ UI/                        # Ventana principal (ShellWindow), DialogService, TableRenderer,
│                             # ModuleExecutionContext, ModuleAction, NativeMessageBox
├─ Assets/wdigger.ico         # Icono de la aplicación
├─ Core/                      # AppHost, CommandExecutor, AppLogger, ConfigManager,
│                             # RestorePointManager, AppSettings, CrashLog
├─ Models/                    # ComponentInfo, SensorReading, TweakDefinition, ...
├─ Services/                  # InventoryService, SensorService, TweakHistoryService
├─ Modules/                   # 13 módulos + ModuleCatalog (tag → módulo)
├─ Views/                     # DashboardPage, ModulePage, ActionCard, ActionCardViewModel
```

## Módulos

| Módulo | Funciones |
|---|---|
| Panel de control | Resumen del sistema, acciones rápidas, carpetas de datos y registro |
| Hardware | Inventario WMI, monitor de sensores, discos y batería |
| Reparación del sistema | SFC, DISM, CHKDSK, Windows Update y caché de Winget |
| Software | Instalación Winget/Chocolatey, búsqueda, inventario y Appx |
| Ajustes y privacidad | Ajustes JSON, vista previa, restauración, historial y reversión |
| Controladores | Exportación, enumeración e instalación de INF |
| Red / Red avanzada | DNS, DHCP, IP, Winsock, TCP/IP, firewall, proxy y conectividad |
| Dispositivos | Impresoras y dispositivos PnP |
| Copias de seguridad | Localización y restauración de archivos Acronis `.tibx` |
| Activación | Estado de licencias de Windows/Office y detección de HEU-KMS existente |
| Recuperación | Windows File Recovery (`winfr`) |
| Eventos y BIOS | Visor de eventos, BIOS, placa base y firmware |
| Discos y limpieza | Espacio, CHKDSK y archivos temporales |

## Publicar el ejecutable portable

### Requisitos en la máquina de compilación

- Windows 10/11 x64.
- [.NET 8 SDK](https://dotnet.microsoft.com/download) o posterior.

El SDK solo es necesario para compilar; **no es necesario en el equipo donde se
use el EXE publicado** (el Windows App SDK viaja dentro del ejecutable).

Desde la raíz del repositorio:

```cmd
publish-portable.cmd
```

O bien, el comando equivalente:

```cmd
dotnet publish "src\WDigger\WDigger.csproj" -c Release -p:PublishProfile=Portable-win-x64
```

El resultado se crea en:

```text
artifacts\portable\WDigger.exe
```

Copia **solamente ese archivo** a una carpeta con permisos de escritura o a un
USB. Al ejecutarlo, acepta el aviso de UAC. Windows aplicará el manifiesto
`requireAdministrator` incluso si el usuario hace doble clic normalmente.

La publicación usa `SelfContained`, `PublishSingleFile`,
`WindowsAppSDKSelfContained`, `WindowsPackageType=None`, `EnableMsixTooling` e
`IncludeAllContentForSelfExtract` (propiedades requeridas por el Windows App SDK
para ejecutables single-file no empaquetados), compresión y no recorta
ensamblados (`PublishTrimmed=false`) para conservar la compatibilidad de WMI,
WinUI 3 y LibreHardwareMonitor. En el primer arranque, el propio EXE extrae sus
dependencias en una carpeta temporal (comportamiento normal del modo
single-file de WinUI).

## CI

`.github/workflows/portable-windows.yml` compila y publica el ejecutable en
`windows-latest` con cada push y pull request, verifica que el artefacto es un
único `WDigger.exe` con el manifiesto elevador incrustado, y lo sube como
artefacto.

## Cambios de la versión 2.0

- **Reescrito de Windows Forms a WinUI 3 (Windows App SDK 1.8).**
- Nueva navegación `NavigationView`, panel de control, tarjetas de acción,
  insignias de seguridad y panel de salida/tabla integrado y contraíble.
- Tema claro/oscuro/automático persistente (`config/ui-settings.json`).
- `config/tweaks.json` y `config/software.json` (formato `{"tweaks": [...]}` /
  `{"packages": [...]}`) ahora se cargan correctamente; antes se esperaba un
  array JSON suelto y la configuración se ignoraba en silencio.
- Barra de título personalizada con insignia de administrador e interruptor
  de modo simulación.
