using System.Globalization;

namespace WDigger.Core;

/// <summary>Accent- and case-insensitive matching; every query word must appear.</summary>
public static class ActionSearch
{
    public static bool Matches(string? query, params string?[] fields)
    {
        var terms = (query ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var comparer = CultureInfo.CurrentCulture.CompareInfo;
        const CompareOptions options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
        return terms.All(term => fields.Any(field => field is not null && comparer.IndexOf(field, term, options) >= 0));
    }
}
