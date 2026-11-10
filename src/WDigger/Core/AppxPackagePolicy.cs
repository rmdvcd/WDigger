using System.Text.RegularExpressions;

namespace WDigger.Core;

/// <summary>Restricts package names embedded in the fixed removal script.</summary>
public static class AppxPackagePolicy
{
    public static bool IsValidFullName(string? name) => name is { Length: > 0 and <= 255 }
        && Regex.IsMatch(name, @"\A[A-Za-z0-9_.-]+\z", RegexOptions.CultureInvariant);
}
