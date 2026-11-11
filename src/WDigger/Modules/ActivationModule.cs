using WDigger.Core;
using WDigger.UI;

namespace WDigger.Modules;

public sealed class ActivationModule : IModule
{
    public string Name => "Activación";
    public string Description => "Estado de licencias de Windows y Office y detección de herramientas existentes.";

    public IReadOnlyList<ModuleAction> CreateActions(AppHost host) => new[]
    {
        new ModuleAction("Comprobar activación de Windows", "Consulta el estado de la licencia mediante slmgr /xpr.",
            (context, token) => RunWindowsLicenseAsync(context, token, "/xpr")),
        new ModuleAction("Activar Windows", "Solicita activación con la clave o licencia digital ya configurada.",
            (context, token) => RunWindowsLicenseAsync(context, token, "/ato"), changesSystem: true,
            confirmation: "Se intentará activar Windows con la licencia configurada. No se instalarán claves nuevas.",
            allowedInElevatedSession: true, risk: OperationRisk.Moderate, commandTimeout: TimeSpan.FromMinutes(15)),
        new ModuleAction("Comprobar activación de Office", "Localiza ospp.vbs y consulta el estado de las licencias de Office.",
            CheckOfficeStatusAsync),
        new ModuleAction("Activar Office", "Solicita activación mediante ospp.vbs con la licencia existente.",
            ActivateOfficeAsync, changesSystem: true,
            confirmation: "Se intentará activar Office con su licencia configurada.", risk: OperationRisk.Moderate),
        new ModuleAction("Detectar HEU-KMS existente", "Solo informa de instalaciones presentes en ubicaciones habituales; no descarga ni ejecuta software.",
            DetectHeuKms)
    };

    private static async Task CheckOfficeStatusAsync(ModuleExecutionContext context, CancellationToken token)
    {
        var script = FindOfficeScript();
        if (script is null)
        {
            context.WriteLine("No se encontró ospp.vbs en las ubicaciones habituales de Office.", OutputKind.Warning);
            context.SetOutcome(OperationOutcome.NotStarted("No se encontró ospp.vbs en las ubicaciones habituales de Office."));
            return;
        }
        await context.RunCommandAsync(CommandSpec.Read(WindowsTool.Cscript, "//Nologo", script, "/dstatus"), token);
    }

    private static async Task ActivateOfficeAsync(ModuleExecutionContext context, CancellationToken token)
    {
        var script = FindOfficeScript();
        if (script is null)
        {
            context.WriteLine("No se encontró ospp.vbs en las ubicaciones habituales de Office.", OutputKind.Warning);
            context.SetOutcome(OperationOutcome.NotStarted("No se encontró ospp.vbs en las ubicaciones habituales de Office."));
            return;
        }
        await context.RunCommandAsync(CommandSpec.Modify(WindowsTool.Cscript, "//Nologo", script, "/act"), token);
    }

    private static Task DetectHeuKms(ModuleExecutionContext context, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var found = new[]
        {
            @"C:\Program Files\HEU_KMS_Activator",
            @"C:\Program Files (x86)\HEU_KMS_Activator",
            @"C:\Tools\HEU_KMS_Activator",
            @"C:\HEU_KMS_Activator"
        }.FirstOrDefault(Directory.Exists);

        if (found is null)
        {
            context.WriteLine("No se detectó HEU-KMS en las ubicaciones habituales.", OutputKind.Warning);
        }
        else
        {
            context.WriteLine($"Instalación detectada: {found}", OutputKind.Success);
            var executable = Directory.EnumerateFiles(found, "*.exe", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (executable is not null) context.WriteLine($"Ejecutable: {Path.GetFileName(executable)}");
        }
        context.WriteLine("Las herramientas de terceros deben utilizarse conforme a las licencias y la legislación aplicable.", OutputKind.Muted);
        return Task.CompletedTask;
    }

    private static string? FindOfficeScript() => new[]
    {
        @"C:\Program Files\Microsoft Office\Office16\ospp.vbs",
        @"C:\Program Files (x86)\Microsoft Office\Office16\ospp.vbs",
        @"C:\Program Files\Microsoft Office\Office15\ospp.vbs",
        @"C:\Program Files (x86)\Microsoft Office\Office15\ospp.vbs"
    }.FirstOrDefault(File.Exists);

    private static Task<CommandResult> RunWindowsLicenseAsync(ModuleExecutionContext context, CancellationToken token, string operation)
        => context.RunCommandAsync(new CommandSpec(WindowsTool.Cscript, new[]
        {
            "//Nologo", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "slmgr.vbs"), operation
        }, operation == "/ato" ? CommandEffect.ModifiesSystem : CommandEffect.ReadOnly), token);
}
