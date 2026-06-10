using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.OppaiDex.Metadata;

public static class CatalogueIdTags
{
    public static IReadOnlyList<string> Create(string? catalogueId)
    {
        if (string.IsNullOrWhiteSpace(catalogueId))
        {
            return [];
        }

        var id = CatalogueIdFormatter.Format(catalogueId)!;
        var prefix = new string(id
            .TakeWhile(char.IsLetter)
            .Select(char.ToUpperInvariant)
            .ToArray());

        return string.IsNullOrWhiteSpace(prefix)
            ? [id]
            : [id, prefix];
    }
}
