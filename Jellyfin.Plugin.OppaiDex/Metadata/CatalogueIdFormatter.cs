using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.OppaiDex.Metadata;

public static class CatalogueIdFormatter
{
    private static readonly Regex CatalogueIdRegex = new(
        @"^(?<prefix>[A-Za-z]{2,12})[\s._-]?(?<number>\d{2,6})$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string? Format(string? catalogueId)
    {
        if (string.IsNullOrWhiteSpace(catalogueId))
        {
            return null;
        }

        var value = catalogueId.Trim();
        var match = CatalogueIdRegex.Match(value);
        return match.Success
            ? string.Concat(
                match.Groups["prefix"].Value,
                "-",
                match.Groups["number"].Value)
                .ToUpperInvariant()
            : value.ToUpperInvariant();
    }
}
