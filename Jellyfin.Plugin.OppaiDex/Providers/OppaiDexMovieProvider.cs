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
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.OppaiDex.Providers;

public sealed class OppaiDexMovieProvider :
    IRemoteMetadataProvider<Movie, MovieInfo>,
    IHasOrder
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OppaiDexMovieProvider> _logger;
    private readonly IReadOnlyList<IPersonMetadataEnricher> _personEnrichers;
    private readonly MetadataSourceRegistry _sourceRegistry;

    public OppaiDexMovieProvider(
        MetadataSourceRegistry sourceRegistry,
        IEnumerable<IPersonMetadataEnricher> personEnrichers,
        IHttpClientFactory httpClientFactory,
        ILogger<OppaiDexMovieProvider> logger)
    {
        _sourceRegistry = sourceRegistry;
        _personEnrichers = personEnrichers
            .OrderBy(enricher => enricher.Order)
            .ToArray();
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public string Name => Constants.PluginName;

    public int Order => 0;

    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(
        MovieInfo searchInfo,
        CancellationToken cancellationToken)
    {
        var results = new List<RemoteSearchResult>();
        var sources = _sourceRegistry
            .GetCandidates(searchInfo.ProviderIds)
            .ToArray();
        var fallbackAttempted = false;

        for (var index = 0; index < sources.Length; index++)
        {
            var source = sources[index];
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
                fallbackAttempted |= LogFallbackAttempt(sources, index, id);
                continue;
            }

            LogFallbackSuccess(fallbackAttempted, source, id);
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
        var sources = _sourceRegistry
            .GetCandidates(info.ProviderIds)
            .ToArray();
        var fallbackAttempted = false;

        for (var index = 0; index < sources.Length; index++)
        {
            var source = sources[index];
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
                LogFallbackSuccess(fallbackAttempted, source, id);
                return await CreateResultAsync(metadata, cancellationToken)
                    .ConfigureAwait(false);
            }

            fallbackAttempted |= LogFallbackAttempt(sources, index, id);
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

    private bool LogFallbackAttempt(
        IReadOnlyList<IMovieMetadataSource> sources,
        int currentIndex,
        string id)
    {
        var nextIndex = currentIndex + 1;
        if (nextIndex >= sources.Count)
        {
            return false;
        }

        _logger.LogInformation(
            "{SourceName} returned no metadata for {MovieId}; trying fallback {FallbackSourceName}.",
            sources[currentIndex].DisplayName,
            id,
            sources[nextIndex].DisplayName);
        return true;
    }

    private void LogFallbackSuccess(
        bool fallbackAttempted,
        IMovieMetadataSource source,
        string id)
    {
        if (!fallbackAttempted)
        {
            return;
        }

        _logger.LogInformation(
            "Fallback {SourceName} found metadata for {MovieId}.",
            source.DisplayName,
            id);
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
