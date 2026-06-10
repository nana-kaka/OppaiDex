using System;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.OppaiDex.Metadata;

public static class CatalogueIdFormatter
{
    private static readonly Regex CatalogueIdRegex = new(
        @"(?<![A-Za-z0-9])(?:(?<fc2>FC2)[\s._-]*(?<ppv>PPV)[\s._-]*(?<fc2Number>\d{5,10})|(?<prefix>[A-Za-z]{2,12})[\s._-]?(?<number>\d{2,7}))(?!\d)",
        RegexOptions.IgnoreCase
        | RegexOptions.CultureInvariant
        | RegexOptions.Compiled);
    private static readonly Regex ExactCatalogueIdRegex = new(
        @"^(?:(?<fc2>FC2)[\s._-]*(?<ppv>PPV)[\s._-]*(?<fc2Number>\d{5,10})|(?<prefix>[A-Za-z]{2,12})[\s._-]?(?<number>\d{2,7}))$",
        RegexOptions.IgnoreCase
        | RegexOptions.CultureInvariant
        | RegexOptions.Compiled);

    public static string? Extract(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var match = CatalogueIdRegex.Match(value);
        return match.Success ? Format(match) : null;
    }

    public static string? Format(string? catalogueId)
    {
        if (string.IsNullOrWhiteSpace(catalogueId))
        {
            return null;
        }

        var value = catalogueId.Trim();
        var match = ExactCatalogueIdRegex.Match(value);
        return match.Success
            ? Format(match)
            : value.ToUpperInvariant();
    }

    public static bool IsFc2Ppv(string? catalogueId)
    {
        return Format(catalogueId)?.StartsWith(
            "FC2-PPV-",
            StringComparison.Ordinal) == true;
    }

    private static string Format(Match match)
    {
        if (match.Groups["fc2"].Success)
        {
            return string.Concat(
                "FC2-PPV-",
                match.Groups["fc2Number"].Value);
        }

        return string.Concat(
            match.Groups["prefix"].Value,
            "-",
            match.Groups["number"].Value)
            .ToUpperInvariant();
    }
}
