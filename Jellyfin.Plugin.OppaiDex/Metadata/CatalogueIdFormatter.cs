using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.OppaiDex.Metadata;

public static partial class CatalogueIdFormatter
{
    public static string? Format(string? catalogueId)
    {
        if (string.IsNullOrWhiteSpace(catalogueId))
        {
            return null;
        }

        var value = catalogueId.Trim();
        var match = CatalogueIdRegex().Match(value);
        return match.Success
            ? string.Concat(
                match.Groups["prefix"].Value,
                "-",
                match.Groups["number"].Value)
                .ToUpperInvariant()
            : value.ToUpperInvariant();
    }

    [GeneratedRegex(
        @"^(?<prefix>[A-Za-z]{2,12})[\s._-]?(?<number>\d{2,6})$",
        RegexOptions.CultureInvariant)]
    private static partial Regex CatalogueIdRegex();
}
