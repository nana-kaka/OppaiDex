using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.OppaiDex.Metadata;

public sealed class MetadataSourceRegistry
{
    private readonly IReadOnlyList<IMovieMetadataSource> _sources;

    public MetadataSourceRegistry(IEnumerable<IMovieMetadataSource> sources)
    {
        _sources = sources
            .Where(source => source.IsEnabled)
            .OrderBy(source => source.Order)
            .ToArray();

        var duplicateKey = _sources
            .GroupBy(source => source.Key, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1)
            ?.Key;
        if (duplicateKey is not null)
        {
            throw new InvalidOperationException(
                $"Multiple metadata sources use the key '{duplicateKey}'.");
        }
    }

    public IEnumerable<IMovieMetadataSource> GetCandidates(
        IReadOnlyDictionary<string, string> providerIds)
    {
        var explicitlySelected = _sources
            .Where(source => providerIds.ContainsKey(source.Key))
            .ToArray();

        return explicitlySelected.Length > 0 ? explicitlySelected : _sources;
    }
}
