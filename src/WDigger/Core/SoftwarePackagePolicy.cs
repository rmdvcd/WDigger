using System.Text.RegularExpressions;
using WDigger.Models;

namespace WDigger.Core;

/// <summary>External package configuration cannot supply arbitrary switches.</summary>
public static class SoftwarePackagePolicy
{
    public static void Validate(SoftwareDefinition package)
    {
        if (!Regex.IsMatch(package.Id ?? "", @"\A[A-Za-z0-9][A-Za-z0-9.+_-]*\z", RegexOptions.CultureInvariant)
            || (!string.Equals(package.Source, "winget", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(package.Source, "chocolatey", StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(package.Arguments)
                && !(string.Equals(package.Source, "winget", StringComparison.OrdinalIgnoreCase)
                     && package.Arguments.Trim() == "--silent")
                && !(string.Equals(package.Source, "chocolatey", StringComparison.OrdinalIgnoreCase)
                     && package.Arguments.Trim() == "-y")))
            throw new InvalidOperationException($"Paquete de configuración no válido: {package.Name}. Se permiten solo ID y opciones conocidas.");
    }
}
