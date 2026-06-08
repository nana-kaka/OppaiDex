using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.OppaiDex.Configuration;
using Jellyfin.Plugin.OppaiDex.Metadata;
using MediaBrowser.Common.Net;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.OppaiDex.Sources.R18;

public sealed class R18MetadataSource : IMovieMetadataSource
{
    private const string PersonImageBaseUrl =
        "https://awsimgsrc.dmm.com/dig/mono/actjpgs/";
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);
    private static readonly Regex MovieIdRegex = new(
        @"(?<![A-Za-z0-9])(?<prefix>[A-Za-z]{2,12})[\s._-]?(?<number>\d{2,6})(?!\d)",
        RegexOptions.IgnoreCase
        | RegexOptions.CultureInvariant
        | RegexOptions.Compiled);
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<R18MetadataSource> _logger;
    private readonly PluginConfigurationAccessor _pluginConfiguration;

    public R18MetadataSource(
        IHttpClientFactory httpClientFactory,
        ILogger<R18MetadataSource> logger,
        PluginConfigurationAccessor pluginConfiguration)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _pluginConfiguration = pluginConfiguration;
    }

    public string Key => R18Provider.Key;

    public string DisplayName => R18Provider.DisplayName;

    public int Order => 0;

    public bool IsEnabled => true;

    public string? GetLookupId(
        IReadOnlyDictionary<string, string> providerIds,
        string? path,
        string? name)
    {
        if (providerIds.TryGetValue(Key, out var providerId)
            && !string.IsNullOrWhiteSpace(providerId))
        {
            return providerId;
        }

        foreach (var candidate in new[] { path, name })
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            var fileName = Path.GetFileNameWithoutExtension(candidate);
            var match = MovieIdRegex.Match(fileName);
            if (match.Success)
            {
                return string.Concat(
                    match.Groups["prefix"].Value,
                    "-",
                    match.Groups["number"].Value);
            }
        }

        return null;
    }

    public async Task<MovieMetadata?> GetMetadataAsync(
        string id,
        CancellationToken cancellationToken)
    {
        var movie = await GetMovieAsync(id, cancellationToken).ConfigureAwait(false);
        return movie is null ? null : MapMovie(movie, id);
    }

    private async Task<R18Movie?> GetMovieAsync(
        string id,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var template = _pluginConfiguration.Current.R18ApiUrlTemplate;
        if (string.IsNullOrWhiteSpace(template)
            || !template.Contains("{id}", StringComparison.Ordinal))
        {
            _logger.LogError(
                "The R18.dev API URL template must contain '{{id}}'.");
            return null;
        }

        var normalizedId = NormalizeId(id);
        var url = template.Replace(
            "{id}",
            Uri.EscapeDataString(normalizedId),
            StringComparison.Ordinal);
        if (!TryGetHttpUri(url, out var requestUri))
        {
            _logger.LogError(
                "The R18.dev API URL template produced an invalid HTTP URL.");
            return null;
        }

        try
        {
            using var httpClient =
                _httpClientFactory.CreateClient(NamedClient.Default);
            using var response = await httpClient
                .GetAsync(requestUri, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "R18.dev returned HTTP {StatusCode} for {MovieId}.",
                    (int)response.StatusCode,
                    normalizedId);
                return null;
            }

            await using var stream = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync<R18Movie>(
                    stream,
                    JsonOptions,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(
                exception,
                "Could not retrieve R18.dev metadata for {MovieId}.",
                normalizedId);
            return null;
        }
        catch (JsonException exception)
        {
            _logger.LogError(
                exception,
                "Could not parse R18.dev metadata for {MovieId}.",
                normalizedId);
            return null;
        }
    }

    private static MovieMetadata MapMovie(R18Movie movie, string fallbackId)
    {
        return new MovieMetadata
        {
            Name = GetTitle(movie),
            OriginalTitle = movie.TitleJapanese,
            Overview = movie.CommentEnglish,
            ReleaseDate = movie.ReleaseDate,
            RuntimeMinutes = movie.RuntimeMinutes,
            Genres = movie.Categories
                .Select(category => category.DisplayName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Studios = GetStudios(movie),
            Tags = string.IsNullOrWhiteSpace(movie.DvdId)
                ? []
                : [movie.DvdId],
            ProviderIds = GetProviderIds(movie, fallbackId),
            People = GetPeople(movie),
            PrimaryImage = CreateImage(
                movie.JacketFullUrl,
                movie.JacketThumbUrl),
            Backdrops = movie.Gallery
                .Select(image => CreateImage(
                    image.FullUrl,
                    image.ThumbnailUrl))
                .Where(image => image is not null)
                .Cast<RemoteImageMetadata>()
                .DistinctBy(
                    image => image.Url,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    private static string GetTitle(R18Movie movie)
    {
        if (!string.IsNullOrWhiteSpace(movie.TitleEnglishUncensored))
        {
            return movie.TitleEnglishUncensored;
        }

        if (!string.IsNullOrWhiteSpace(movie.TitleEnglish))
        {
            return movie.TitleEnglish;
        }

        return movie.TitleJapanese
            ?? movie.DvdId
            ?? movie.ContentId
            ?? "Unknown";
    }

    private static IReadOnlyDictionary<string, string> GetProviderIds(
        R18Movie movie,
        string fallbackId)
    {
        var providerIds = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase)
        {
            [R18Provider.Key] =
                movie.DvdId ?? movie.ContentId ?? fallbackId
        };

        if (!string.IsNullOrWhiteSpace(movie.ContentId))
        {
            providerIds[R18Provider.ContentIdKey] = movie.ContentId;
        }

        return providerIds;
    }

    private static IReadOnlyList<string> GetStudios(R18Movie movie)
    {
        return new[]
            {
                movie.MakerNameEnglish ?? movie.MakerNameJapanese,
                movie.LabelNameEnglish ?? movie.LabelNameJapanese
            }
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<MoviePersonMetadata> GetPeople(R18Movie movie)
    {
        return MapPeople(
                movie.Actresses
                    .Concat(movie.Actors)
                    .Concat(movie.Histrions),
                PersonKind.Actor)
            .Concat(MapPeople(movie.Directors, PersonKind.Director))
            .Concat(MapPeople(movie.Authors, PersonKind.Writer))
            .ToArray();
    }

    private static IEnumerable<MoviePersonMetadata> MapPeople(
        IEnumerable<R18Person> people,
        PersonKind kind)
    {
        return people
            .Where(person => !string.IsNullOrWhiteSpace(person.DisplayName))
            .DistinctBy(
                person => person.Id > 0
                    ? person.Id.ToString(CultureInfo.InvariantCulture)
                    : person.DisplayName!,
                StringComparer.OrdinalIgnoreCase)
            .Select(person => new MoviePersonMetadata
            {
                Name = person.DisplayName!,
                Kind = kind,
                ImageUrl = GetPersonImageUrl(person.ImageUrl),
                ProviderIds = person.Id > 0
                    ? new Dictionary<string, string>(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        [R18Provider.Key] =
                            person.Id.ToString(CultureInfo.InvariantCulture)
                    }
                    : new Dictionary<string, string>(
                        StringComparer.OrdinalIgnoreCase)
            });
    }

    private static RemoteImageMetadata? CreateImage(
        string? url,
        string? thumbnailUrl)
    {
        var fullUrl = GetHttpUrl(url) ?? GetHttpUrl(thumbnailUrl);
        if (fullUrl is null)
        {
            return null;
        }

        return new RemoteImageMetadata
        {
            Url = fullUrl,
            ThumbnailUrl = GetHttpUrl(thumbnailUrl)
        };
    }

    private static string? GetPersonImageUrl(string? imageUrl)
    {
        var absoluteUrl = GetHttpUrl(imageUrl);
        if (absoluteUrl is not null)
        {
            return absoluteUrl;
        }

        var fileName = Path.GetFileName(imageUrl);
        return string.IsNullOrWhiteSpace(fileName)
            ? null
            : string.Concat(
                PersonImageBaseUrl,
                Uri.EscapeDataString(fileName));
    }

    private static string? GetHttpUrl(string? url)
    {
        return TryGetHttpUri(url, out var uri) ? uri.ToString() : null;
    }

    private static bool TryGetHttpUri(string? url, out Uri uri)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var candidate)
            && (candidate.Scheme == Uri.UriSchemeHttp
                || candidate.Scheme == Uri.UriSchemeHttps))
        {
            uri = candidate;
            return true;
        }

        uri = null!;
        return false;
    }

    private static string NormalizeId(string id)
    {
        return string.Concat(id.Where(char.IsLetterOrDigit))
            .ToLower(CultureInfo.InvariantCulture);
    }
}
