using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.OppaiDex.Configuration;
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
    private readonly PluginConfigurationAccessor _pluginConfiguration;
    private readonly MetadataSourceRegistry _sourceRegistry;

    public OppaiDexMovieProvider(
        MetadataSourceRegistry sourceRegistry,
        IEnumerable<IPersonMetadataEnricher> personEnrichers,
        PluginConfigurationAccessor pluginConfiguration,
        IHttpClientFactory httpClientFactory,
        ILogger<OppaiDexMovieProvider> logger)
    {
        _sourceRegistry = sourceRegistry;
        _personEnrichers = personEnrichers
            .OrderBy(enricher => enricher.Order)
            .ToArray();
        _pluginConfiguration = pluginConfiguration;
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
                var nextFallbackAvailable = LogFallbackAttempt(
                    sources,
                    index,
                    id);
                if (nextFallbackAvailable)
                {
                    fallbackAttempted = true;
                }
                else
                {
                    LogFallbackFailure(fallbackAttempted, source, id);
                }

                continue;
            }

            LogFallbackSuccess(fallbackAttempted, source, id);
            results.Add(new RemoteSearchResult
            {
                Name = GetDisplayName(metadata),
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

            var nextFallbackAvailable = LogFallbackAttempt(sources, index, id);
            if (nextFallbackAvailable)
            {
                fallbackAttempted = true;
            }
            else
            {
                LogFallbackFailure(fallbackAttempted, source, id);
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

    private void LogFallbackFailure(
        bool fallbackAttempted,
        IMovieMetadataSource source,
        string id)
    {
        if (!fallbackAttempted)
        {
            return;
        }

        _logger.LogInformation(
            "No metadata source found metadata for {MovieId}; last fallback {SourceName} returned no result.",
            id,
            source.DisplayName);
    }

    private async Task<MetadataResult<Movie>> CreateResultAsync(
        MovieMetadata metadata,
        CancellationToken cancellationToken)
    {
        var item = new Movie
        {
            Name = GetDisplayName(metadata),
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

        var enrichedPeople = await EnrichPeopleAsync(
                metadata.People,
                cancellationToken)
            .ConfigureAwait(false);
        foreach (var person in enrichedPeople.Select(CreatePersonInfo))
        {
            result.AddPerson(person);
        }

        return result;
    }

    private string GetDisplayName(MovieMetadata metadata)
    {
        if (!_pluginConfiguration.Current.PrefixMovieTitlesWithCatalogueId
            || string.IsNullOrWhiteSpace(metadata.CatalogueId))
        {
            return metadata.Name;
        }

        var prefix = $"[{metadata.CatalogueId}]";
        return metadata.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? metadata.Name
            : $"{prefix} {metadata.Name}";
    }

    private async Task<IReadOnlyList<MoviePersonMetadata>> EnrichPeopleAsync(
        IReadOnlyList<MoviePersonMetadata> people,
        CancellationToken cancellationToken)
    {
        var enrichedPeople = new List<MoviePersonMetadata>(people.Count);
        var enrichmentTasks = people.Select(
            person => EnrichPersonAsync(person, cancellationToken));
        foreach (var enrichmentTask in enrichmentTasks)
        {
            enrichedPeople.Add(
                await enrichmentTask.ConfigureAwait(false));
        }

        return enrichedPeople;
    }

    private async Task<MoviePersonMetadata> EnrichPersonAsync(
        MoviePersonMetadata person,
        CancellationToken cancellationToken)
    {
        var enrichedPerson = person;
        foreach (var enricher in _personEnrichers)
        {
            enrichedPerson = await enricher
                .EnrichAsync(enrichedPerson, cancellationToken)
                .ConfigureAwait(false);
        }

        return enrichedPerson;
    }

    private static PersonInfo CreatePersonInfo(MoviePersonMetadata person)
    {
        return new PersonInfo
        {
            Name = person.Name,
            Type = person.Kind,
            ImageUrl = person.ImageUrl,
            ProviderIds = new Dictionary<string, string>(
                person.ProviderIds,
                StringComparer.OrdinalIgnoreCase)
        };
    }
}
