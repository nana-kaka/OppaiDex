using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.OppaiDex.Metadata;

namespace Jellyfin.Plugin.OppaiDex.Sources.Warashi;

public sealed class WarashiPersonMetadataEnricher : IPersonMetadataEnricher
{
    private readonly WarashiClient _warashiClient;

    public WarashiPersonMetadataEnricher(WarashiClient warashiClient)
    {
        _warashiClient = warashiClient;
    }

    public int Order => 0;

    public async Task<MoviePersonMetadata> EnrichAsync(
        MoviePersonMetadata person,
        CancellationToken cancellationToken)
    {
        if (person.Kind != PersonKind.Actor)
        {
            return person;
        }

        var warashiPerson = await _warashiClient
            .FindExactAsync(person.Name, cancellationToken)
            .ConfigureAwait(false);
        if (warashiPerson is null)
        {
            return person;
        }

        var providerIds = new Dictionary<string, string>(
            person.ProviderIds,
            StringComparer.OrdinalIgnoreCase)
        {
            [WarashiProvider.Key] = warashiPerson.Id
        };

        return new MoviePersonMetadata
        {
            Name = person.Name,
            Kind = person.Kind,
            ImageUrl = warashiPerson.ImageUrls.FirstOrDefault()
                ?? person.ImageUrl,
            ProviderIds = providerIds
        };
    }
}
