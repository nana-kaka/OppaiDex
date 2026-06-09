using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.OppaiDex.Configuration;
using Jellyfin.Plugin.OppaiDex.Metadata;
using MediaBrowser.Common.Net;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.OppaiDex.Sources.JavDatabase;

public sealed class JavDatabaseMetadataSource :
    IMovieMetadataSource,
    IDisposable
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(6);
    private static readonly Regex MovieIdRegex = new(
        @"(?<![A-Za-z0-9])(?<prefix>[A-Za-z]{2,12})[\s._-]?(?<number>\d{2,6})(?!\d)",
        RegexOptions.IgnoreCase
        | RegexOptions.CultureInvariant
        | RegexOptions.Compiled);
    private static readonly Regex RuntimeRegex = new(
        @"\d+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private readonly ConcurrentDictionary<string, CachedMetadata> _cache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<JavDatabaseMetadataSource> _logger;
    private readonly PluginConfigurationAccessor _pluginConfiguration;
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private DateTimeOffset _nextRequestAt = DateTimeOffset.MinValue;

    public JavDatabaseMetadataSource(
        IHttpClientFactory httpClientFactory,
        ILogger<JavDatabaseMetadataSource> logger,
        PluginConfigurationAccessor pluginConfiguration)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _pluginConfiguration = pluginConfiguration;
    }

    public string Key => JavDatabaseProvider.Key;

    public string DisplayName => JavDatabaseProvider.DisplayName;

    public int Order => 100;

    public bool IsEnabled =>
        _pluginConfiguration.Current.JavDatabaseEnabled;

    public void Dispose()
    {
        _requestGate.Dispose();
    }

    public string? GetLookupId(
        IReadOnlyDictionary<string, string> providerIds,
        string? path,
        string? name)
    {
        if (providerIds.TryGetValue(Key, out var providerId)
            && !string.IsNullOrWhiteSpace(providerId))
        {
            return NormalizeId(providerId);
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
                    match.Groups["number"].Value)
                    .ToUpperInvariant();
            }
        }

        return null;
    }

    public async Task<MovieMetadata?> GetMetadataAsync(
        string id,
        CancellationToken cancellationToken)
    {
        var normalizedId = NormalizeId(id);
        if (string.IsNullOrWhiteSpace(normalizedId))
        {
            return null;
        }

        if (_cache.TryGetValue(normalizedId, out var cached)
            && cached.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return cached.Metadata;
        }

        if (!TryGetBaseUri(out var baseUri))
        {
            return null;
        }

        await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cache.TryGetValue(normalizedId, out cached)
                && cached.ExpiresAt > DateTimeOffset.UtcNow)
            {
                return cached.Metadata;
            }

            var delay = _nextRequestAt - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }

            _nextRequestAt = DateTimeOffset.UtcNow.AddMilliseconds(
                Math.Clamp(
                    _pluginConfiguration.Current
                        .JavDatabaseRequestDelayMilliseconds,
                    0,
                    60000));

            var requestUri = new Uri(
                baseUri,
                $"/movies/{normalizedId.ToLowerInvariant()}/");
            var metadata = await FetchMetadataAsync(
                    requestUri,
                    normalizedId,
                    baseUri,
                    cancellationToken)
                .ConfigureAwait(false);
            if (metadata is not null)
            {
                _cache[normalizedId] = new CachedMetadata(
                    metadata,
                    DateTimeOffset.UtcNow.Add(CacheLifetime));
            }

            return metadata;
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private async Task<MovieMetadata?> FetchMetadataAsync(
        Uri requestUri,
        string expectedId,
        Uri baseUri,
        CancellationToken cancellationToken)
    {
        try
        {
            using var client =
                _httpClientFactory.CreateClient(NamedClient.Default);
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            request.Headers.UserAgent.ParseAdd(
                "Mozilla/5.0 (compatible; OppaiDex/1.0)");
            using var response = await client
                .SendAsync(request, cancellationToken)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "JavDatabase returned HTTP {StatusCode} for {MovieId}.",
                    (int)response.StatusCode,
                    expectedId);
                return null;
            }

            var html = await response.Content
                .ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);
            var document = await new HtmlParser()
                .ParseDocumentAsync(html, cancellationToken)
                .ConfigureAwait(false);
            return ParseMetadata(document, expectedId, baseUri);
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(
                exception,
                "Could not retrieve JavDatabase metadata for {MovieId}.",
                expectedId);
            return null;
        }
    }

    private MovieMetadata? ParseMetadata(
        IDocument document,
        string expectedId,
        Uri baseUri)
    {
        var fields = document
            .QuerySelectorAll("p.mb-1")
            .Select(ParseField)
            .Where(field => field is not null)
            .Cast<ParsedField>()
            .GroupBy(
                field => field.Key,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);

        var dvdId = GetFieldValue(fields, "DVD ID");
        if (!IdsEqual(dvdId, expectedId))
        {
            _logger.LogWarning(
                "JavDatabase returned DVD ID {ActualId} while {ExpectedId} was requested.",
                dvdId,
                expectedId);
            return null;
        }

        var title = GetFieldValue(fields, "Title");
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var contentId = GetFieldValue(fields, "Content ID");
        var genres = GetLinks(fields, "Genre(s)");
        var studios = GetLinks(fields, "Studio");
        var people = GetPeople(document, baseUri)
            .Concat(GetDirectors(fields))
            .ToArray();
        var releaseDate = DateTime.TryParseExact(
            GetFieldValue(fields, "Release Date"),
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsedDate)
            ? parsedDate
            : (DateTime?)null;
        var runtimeMatch = RuntimeRegex.Match(
            GetFieldValue(fields, "Runtime") ?? string.Empty);
        var runtime = runtimeMatch.Success
            && int.TryParse(
                runtimeMatch.Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var parsedRuntime)
            ? parsedRuntime
            : (int?)null;

        return new MovieMetadata
        {
            Name = title,
            ReleaseDate = releaseDate,
            RuntimeMinutes = runtime,
            Genres = genres,
            Studios = studios,
            Tags = string.IsNullOrWhiteSpace(contentId)
                ? []
                : [contentId],
            ProviderIds = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase)
            {
                [JavDatabaseProvider.Key] = dvdId!
            },
            People = people,
            PrimaryImage = CreateImage(
                baseUri,
                document.QuerySelector(
                    "a[data-source=poster] img")?.GetAttribute("src"),
                document.QuerySelector(
                    "#thumbnailContainer img")?.GetAttribute("src")),
            Backdrops = document
                .QuerySelectorAll(
                    "a[data-bs-target='#lightboxModal'][data-image-src]")
                .Select(element => CreateImage(
                    baseUri,
                    element.GetAttribute("data-image-src"),
                    element.QuerySelector("img")?.GetAttribute("src")))
                .Where(image => image is not null)
                .Cast<RemoteImageMetadata>()
                .DistinctBy(
                    image => image.Url,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    private static ParsedField? ParseField(IElement paragraph)
    {
        var label = paragraph.QuerySelector("b");
        var key = label?.TextContent.Trim().TrimEnd(':');
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var text = paragraph.TextContent.Trim();
        var labelText = label!.TextContent.Trim();
        var value = text.StartsWith(labelText, StringComparison.OrdinalIgnoreCase)
            ? text[labelText.Length..].Trim()
            : text;

        return new ParsedField(key, value, paragraph);
    }

    private static string? GetFieldValue(
        IReadOnlyDictionary<string, ParsedField> fields,
        string key)
    {
        return fields.TryGetValue(key, out var field)
            && !string.IsNullOrWhiteSpace(field.Value)
            ? field.Value
            : null;
    }

    private static IReadOnlyList<string> GetLinks(
        IReadOnlyDictionary<string, ParsedField> fields,
        string key)
    {
        return fields.TryGetValue(key, out var field)
            ? field.Element.QuerySelectorAll("a")
                .Select(link => link.TextContent.Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : [];
    }

    private static IReadOnlyList<MoviePersonMetadata> GetPeople(
        IDocument document,
        Uri baseUri)
    {
        return document
            .QuerySelectorAll(".idol-thumb")
            .Select(element =>
            {
                var card = element.Closest(".card");
                var name = card?.QuerySelector("p a")?.TextContent.Trim();
                var imageUrl = GetAbsoluteHttpUrl(
                    baseUri,
                    element.QuerySelector("img")?.GetAttribute("src"));
                return string.IsNullOrWhiteSpace(name)
                    ? null
                    : new MoviePersonMetadata
                    {
                        Name = name,
                        Kind = PersonKind.Actor,
                        ImageUrl = imageUrl
                    };
            })
            .Where(person => person is not null)
            .Cast<MoviePersonMetadata>()
            .DistinctBy(
                person => person.Name,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<MoviePersonMetadata> GetDirectors(
        IReadOnlyDictionary<string, ParsedField> fields)
    {
        if (!fields.TryGetValue("Director", out var field))
        {
            return [];
        }

        var names = field.Element.QuerySelectorAll("a")
            .Select(link => link.TextContent.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        if (names.Length == 0)
        {
            names = field.Value
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();
        }

        return names
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name => new MoviePersonMetadata
            {
                Name = name,
                Kind = PersonKind.Director
            });
    }

    private static RemoteImageMetadata? CreateImage(
        Uri baseUri,
        string? url,
        string? thumbnailUrl)
    {
        var fullUrl = GetAbsoluteHttpUrl(baseUri, url)
            ?? GetAbsoluteHttpUrl(baseUri, thumbnailUrl);
        return fullUrl is null
            ? null
            : new RemoteImageMetadata
            {
                Url = fullUrl,
                ThumbnailUrl = GetAbsoluteHttpUrl(baseUri, thumbnailUrl)
            };
    }

    private bool TryGetBaseUri(out Uri baseUri)
    {
        var value = _pluginConfiguration.Current.JavDatabaseBaseUrl;
        if (IsEnabled
            && Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp
                || uri.Scheme == Uri.UriSchemeHttps))
        {
            baseUri = uri;
            return true;
        }

        baseUri = null!;
        return false;
    }

    private static string? GetAbsoluteHttpUrl(Uri baseUri, string? value)
    {
        return Uri.TryCreate(baseUri, value, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp
                || uri.Scheme == Uri.UriSchemeHttps)
            ? uri.ToString()
            : null;
    }

    private static string NormalizeId(string id)
    {
        var match = MovieIdRegex.Match(id);
        return match.Success
            ? string.Concat(
                match.Groups["prefix"].Value,
                "-",
                match.Groups["number"].Value)
                .ToUpperInvariant()
            : id.Trim().ToUpperInvariant();
    }

    private static bool IdsEqual(string? left, string right)
    {
        return string.Equals(
            NormalizeId(left ?? string.Empty),
            NormalizeId(right),
            StringComparison.OrdinalIgnoreCase);
    }

    private sealed record ParsedField(
        string Key,
        string Value,
        IElement Element);

    private sealed record CachedMetadata(
        MovieMetadata Metadata,
        DateTimeOffset ExpiresAt);
}
