using System.Reflection;
using System.Text;
using System.Text.Json;

namespace WDigger.Core;

/// <summary>
/// Identifiers for reviewed PowerShell programs embedded in WDigger. Modules
/// cannot submit a script body; they can only provide the declared arguments.
/// </summary>
public enum PowerShellScriptId
{
    AppxInventory,
    RemoveCurrentUserAppx,
    RenameComputer,
    ExportWindowsDrivers,
    EnablePnpDevice,
    DisablePnpDevice,
    ResetWindowsUpdate
}

/// <summary>Builds a non-interactive invocation of a versioned embedded script.</summary>
public static class PowerShellScriptCatalog
{
    private const string ResourcePrefix = "WDigger.PowerShell.Scripts.";

    public static CommandSpec Create(PowerShellScriptId script, IReadOnlyList<string>? arguments = null,
        CommandEffect effect = CommandEffect.ReadOnly, TimeSpan? timeout = null)
    {
        var values = arguments?.ToArray() ?? Array.Empty<string>();
        if (values.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Los argumentos de PowerShell no pueden estar vacíos.", nameof(arguments));
        }
        if (values.Length != ExpectedArgumentCount(script))
        {
            throw new ArgumentException($"El script {script} no acepta ese número de argumentos.", nameof(arguments));
        }

        var resourceName = ResourcePrefix + script + ".ps1";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"No se encontró el script revisado '{script}'.");
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var source = reader.ReadToEnd();
        if (string.IsNullOrWhiteSpace(source))
        {
            throw new InvalidOperationException($"El script revisado '{script}' está vacío.");
        }

        // Source and argument array are both Base64 encoded. User input is never
        // concatenated into PowerShell syntax; it is supplied to a fixed param block.
        var sourceBase64 = Convert.ToBase64String(Encoding.Unicode.GetBytes(source));
        var argumentsJson = JsonSerializer.Serialize(values);
        var argumentsBase64 = Convert.ToBase64String(Encoding.Unicode.GetBytes(argumentsJson));
        var wrapper = "Set-StrictMode -Version Latest; $ErrorActionPreference = 'Stop'; " +
            "$wdiggerSource = [Text.Encoding]::Unicode.GetString([Convert]::FromBase64String('" + sourceBase64 + "')); " +
            "$wdiggerJson = [Text.Encoding]::Unicode.GetString([Convert]::FromBase64String('" + argumentsBase64 + "')); " +
            "$wdiggerArguments = @($wdiggerJson | ConvertFrom-Json); " +
            "& ([ScriptBlock]::Create($wdiggerSource)) @wdiggerArguments";
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(wrapper));
        return new CommandSpec(WindowsTool.PowerShell, new[]
        {
            "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", encoded
        }, effect, timeout);
    }

    private static int ExpectedArgumentCount(PowerShellScriptId script) => script switch
    {
        PowerShellScriptId.AppxInventory => 0,
        PowerShellScriptId.ResetWindowsUpdate => 0,
        _ => 1
    };
}
