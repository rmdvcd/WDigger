# WDigger

WDigger es una herramienta **nativa de Windows** para diagnóstico y reparación,
dirigida a técnicos de PC y portátiles. Es una alternativa *offline-first* a
herramientas como WinUtil, con inventario WMI, sensores en tiempo real, estado
SMART, batería y utilidades de mantenimiento.

La interfaz usa **Windows Forms sobre .NET 8**: navegación lateral, cuadros de
diálogo de Windows, tablas, salida de comandos integrada, progreso y botón de
cancelación. Ya no necesita una ventana de terminal ni Spectre.Console.

> **Herramienta de uso personal.** No está destinada a distribución comercial.
> WDigger solicita siempre elevación mediante UAC y no continúa sin privilegios
> de administrador.

## Características de la aplicación

- **Visual nativa:** controles de Windows, fuente Segoe UI, DPI por monitor y
  compatibilidad con Windows 10/11.
- **Portable:** un único `WDigger.exe` x64 con .NET y las dependencias incluidas.
  No necesita instalador ni un runtime de .NET previamente instalado.
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
  copias editables en `config/`. En un USB de solo lectura puede seguir usando
  los valores incrustados.

## Stack

| Capa | Tecnología |
|---|---|
| Lenguaje / runtime | C# 12, .NET 8, Windows x64 |
| Interfaz | Windows Forms nativo, alto DPI PerMonitorV2 |
| Sensores | LibreHardwareMonitorLib |
| Inventario / SMART / batería / BIOS | System.Management (WMI) |
| Comandos | DISM, SFC, CHKDSK, netsh, PowerShell, Winget, pnputil, winfr, wevtutil, slmgr y ospp.vbs |
| Configuración | JSON editable en `config/`, con valores incrustados de respaldo |
| Auditoría | Registro por sesión en `logs/`, incluyendo stdout, stderr, código y duración |

## Módulos

| Módulo | Funciones |
|---|---|
| Panel de control | Resumen del sistema y acceso a registros/configuración |
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
use el EXE publicado**.

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

La publicación usa `SelfContained`, `PublishSingleFile`, compresión y no recorta
ensamblados (`PublishTrimmed=false`) para conservar la compatibilidad de WMI,
Windows Forms y LibreHardwareMonitor.

### Estructura después del primer uso

```text
USB:\WDigger\
├── WDigger.exe
├── config\
│   ├── tweaks.json       # Copia editable; opcional
│   └── software.json     # Copia editable; opcional
└── logs\
    ├── WDigger_yyyyMMdd_HHmmss.log
    └── applied-tweaks.json
```

Los archivos `config/` y `logs/` se generan en tiempo de ejecución; no son
archivos adicionales necesarios para distribuir la aplicación.

## Desarrollo

```cmd
git clone <repo-url> WDigger
cd WDigger
dotnet restore
dotnet build
dotnet run --project "src\WDigger\WDigger.csproj"
```

Para iniciar en modo simulación:

```cmd
dotnet run --project "src\WDigger\WDigger.csproj" -- --dry-run
```

También se puede activar **Modo simulación** desde el encabezado de la ventana.
La elevación sigue siendo obligatoria en modo simulación para mantener un único
modelo de ejecución y permitir las consultas de hardware.

## Modo simulación y seguridad

- En modo simulación, los comandos que modificarían el sistema se muestran y se
  registran, pero no se ejecutan.
- Las simulaciones de ajustes **no** se agregan al historial de cambios reales.
- En modo real, las acciones que modifican el sistema solicitan confirmación y
  muestran una advertencia específica cuando existe riesgo de pérdida de datos.
- Antes de aplicar o revertir un ajuste reversible, WDigger intenta crear un
  punto de restauración de Windows. Si no puede crearlo, lo informa.
- El historial real se guarda en `logs/applied-tweaks.json` para permitir la
  reversión posterior.
- Cada comando se audita con su salida completa en
  `logs/WDigger_yyyyMMdd_HHmmss.log`.
- Cierra las aplicaciones y guarda el trabajo antes de reparaciones, cambios de
  red, controladores, limpieza o restauración de copias.

## Configuración

- `config/tweaks.json`: acciones de registro o comandos, con una lista opcional
  `rollback` por ajuste.
- `config/software.json`: paquetes predeterminados de Winget/Chocolatey.

Las copias externas tienen prioridad sobre los valores incrustados. Después de
editarlas, reinicia WDigger para volver a cargarlas. Si un archivo externo tiene
JSON inválido, el error queda en el registro y ese catálogo se carga vacío.

## Estructura del código

```text
WDigger/
├── publish-portable.cmd
├── config/                       # Definiciones fuente y editables
└── src/WDigger/
    ├── WDigger.csproj            # WinExe, Windows Forms y single-file
    ├── app.manifest              # requireAdministrator + Windows 10/11 + DPI
    ├── Program.cs                # Arranque STA, UAC y errores globales
    ├── Properties/PublishProfiles/
    │   └── Portable-win-x64.pubxml
    ├── Core/                     # Servicios, procesos, configuración y logging
    ├── Models/
    ├── Services/                 # WMI, sensores e historial
    ├── UI/
    │   ├── Shell.cs              # Ventana nativa principal
    │   ├── ModuleAction.cs
    │   ├── ModuleExecutionContext.cs
    │   └── NativeDialogs.cs
    └── Modules/                  # Acciones asíncronas de cada herramienta
```

Para agregar un módulo, implementa `IModule`, expone sus `ModuleAction` mediante
`CreateActions(AppHost)` y añade una instancia en `UI/Shell.cs`. Usa
`ModuleExecutionContext.RunCommandAsync` para conservar dry-run, cancelación,
salida integrada y auditoría.

## Licencia

Herramienta de uso personal. No se permite distribución comercial.
