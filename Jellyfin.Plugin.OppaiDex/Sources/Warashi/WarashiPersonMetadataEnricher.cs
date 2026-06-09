using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.OppaiDex.Metadata;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.OppaiDex.Sources.Warashi;

public sealed class WarashiPersonMetadataEnricher : IPersonMetadataEnricher
{
    private readonly WarashiClient _warashiClient;
    private readonly ILogger<WarashiPersonMetadataEnricher> _logger;

    public WarashiPersonMetadataEnricher(
        WarashiClient warashiClient,
        ILogger<WarashiPersonMetadataEnricher> logger)
    {
        _warashiClient = warashiClient;
        _logger = logger;
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
        var preferredImage = warashiPerson.HasPreferredImages
            ? warashiPerson.ImageUrls.FirstOrDefault()
            : null;

        if (preferredImage is not null)
        {
            _logger.LogInformation(
                "WAPdB matched {PersonName} as {WarashiId}; using the preferred WAPdB portrait.",
                person.Name,
                warashiPerson.Id);
        }
        else
        {
            _logger.LogInformation(
                "WAPdB matched {PersonName} as {WarashiId}, but only a low-resolution mini-profile image is available; keeping the existing portrait.",
                person.Name,
                warashiPerson.Id);
        }

        return new MoviePersonMetadata
        {
            Name = person.Name,
            Kind = person.Kind,
            ImageUrl = preferredImage ?? person.ImageUrl,
            ProviderIds = providerIds
        };
    }
}
