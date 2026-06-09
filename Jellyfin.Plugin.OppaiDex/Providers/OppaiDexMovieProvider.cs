using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.OppaiDex.Metadata;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.OppaiDex.Providers;

public sealed class OppaiDexMovieProvider :
    IRemoteMetadataProvider<Movie, MovieInfo>,
    IHasOrder
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IReadOnlyList<IPersonMetadataEnricher> _personEnrichers;
    private readonly MetadataSourceRegistry _sourceRegistry;

    public OppaiDexMovieProvider(
        MetadataSourceRegistry sourceRegistry,
        IEnumerable<IPersonMetadataEnricher> personEnrichers,
        IHttpClientFactory httpClientFactory)
    {
        _sourceRegistry = sourceRegistry;
        _personEnrichers = personEnrichers
            .OrderBy(enricher => enricher.Order)
            .ToArray();
        _httpClientFactory = httpClientFactory;
    }

    public string Name => Constants.PluginName;

    public int Order => 0;

    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(
        MovieInfo searchInfo,
        CancellationToken cancellationToken)
    {
        var results = new List<RemoteSearchResult>();

        foreach (var source in _sourceRegistry.GetCandidates(searchInfo.ProviderIds))
        {
            var id = source.GetLookupId(
                searchInfo.ProviderIds,
                searchInfo.Path,
                searchInfo.Name);
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var metadata = await source
                .GetMetadataAsync(id, cancellationToken)
                .ConfigureAwait(false);
            if (metadata is null)
            {
                continue;
            }

            results.Add(new RemoteSearchResult
            {
                Name = metadata.Name,
                ImageUrl = metadata.PrimaryImage?.ThumbnailUrl
                    ?? metadata.PrimaryImage?.Url,
                Overview = metadata.Overview,
                ProductionYear = metadata.ReleaseDate?.Year,
                ProviderIds = new Dictionary<string, string>(
                    metadata.ProviderIds,
                    StringComparer.OrdinalIgnoreCase)
            });

            break;
        }

        return results;
    }

    public async Task<MetadataResult<Movie>> GetMetadata(
        MovieInfo info,
        CancellationToken cancellationToken)
    {
        foreach (var source in _sourceRegistry.GetCandidates(info.ProviderIds))
        {
            var id = source.GetLookupId(info.ProviderIds, info.Path, info.Name);
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var metadata = await source
                .GetMetadataAsync(id, cancellationToken)
                .ConfigureAwait(false);
            if (metadata is not null)
            {
                return await CreateResultAsync(metadata, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        return new MetadataResult<Movie>();
    }

    public Task<HttpResponseMessage> GetImageResponse(
        string url,
        CancellationToken cancellationToken)
    {
        return _httpClientFactory
            .CreateClient(NamedClient.Default)
            .GetAsync(url, cancellationToken);
    }

    private async Task<MetadataResult<Movie>> CreateResultAsync(
        MovieMetadata metadata,
        CancellationToken cancellationToken)
    {
        var item = new Movie
        {
            Name = metadata.Name,
            OriginalTitle = metadata.OriginalTitle,
            Overview = metadata.Overview,
            PremiereDate = metadata.ReleaseDate,
            ProductionYear = metadata.ReleaseDate?.Year,
            RunTimeTicks = metadata.RuntimeMinutes is > 0
                ? TimeSpan.FromMinutes(metadata.RuntimeMinutes.Value).Ticks
                : null,
            Genres = metadata.Genres.ToArray(),
            Studios = metadata.Studios.ToArray(),
            Tags = metadata.Tags.ToArray(),
            ProviderIds = new Dictionary<string, string>(
                metadata.ProviderIds,
                StringComparer.OrdinalIgnoreCase)
        };

        var result = new MetadataResult<Movie>
        {
            HasMetadata = true,
            Item = item
        };

        foreach (var person in metadata.People)
        {
            var enrichedPerson = person;
            foreach (var enricher in _personEnrichers)
            {
                enrichedPerson = await enricher
                    .EnrichAsync(enrichedPerson, cancellationToken)
                    .ConfigureAwait(false);
            }

            result.AddPerson(new PersonInfo
            {
                Name = enrichedPerson.Name,
                Type = enrichedPerson.Kind,
                ImageUrl = enrichedPerson.ImageUrl,
                ProviderIds = new Dictionary<string, string>(
                    enrichedPerson.ProviderIds,
                    StringComparer.OrdinalIgnoreCase)
            });
        }

        return result;
    }
}
