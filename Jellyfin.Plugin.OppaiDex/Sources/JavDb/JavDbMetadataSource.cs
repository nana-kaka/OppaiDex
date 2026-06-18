using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Jellyfin.Plugin.OppaiDex.Configuration;
using Jellyfin.Plugin.OppaiDex.Metadata;
using MediaBrowser.Common.Net;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.OppaiDex.Sources.JavDb;

public sealed class JavDbMetadataSource : IMovieMetadataSource, IDisposable
{
    private static readonly TimeSpan AccessBlockedCooldown =
        TimeSpan.FromMinutes(30);
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(6);
    private static readonly TimeSpan NotFoundCacheLifetime =
        TimeSpan.FromMinutes(30);
    private static readonly string[] ReleaseDateFormats =
    [
        "yyyy-MM-dd",
        "MM/dd/yyyy"
    ];
    private readonly ConcurrentDictionary<string, CachedMetadata> _cache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<JavDbMetadataSource> _logger;
    private readonly PluginConfigurationAccessor _pluginConfiguration;
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private DateTimeOffset _blockedUntil = DateTimeOffset.MinValue;
    private DateTimeOffset _nextRequestAt = DateTimeOffset.MinValue;

    public JavDbMetadataSource(
        IHttpClientFactory httpClientFactory,
        ILogger<JavDbMetadataSource> logger,
        PluginConfigurationAccessor pluginConfiguration)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _pluginConfiguration = pluginConfiguration;
    }

    public string Key => JavDbProvider.Key;

    public string DisplayName => JavDbProvider.DisplayName;

    public int Order => 200;

    public bool IsEnabled => _pluginConfiguration.Current.JavDbEnabled;

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
            var catalogueId = CatalogueIdFormatter.Extract(fileName);
            if (catalogueId is not null)
            {
                return catalogueId;
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

        if (TryGetCachedMetadata(normalizedId, out var cachedMetadata))
        {
            return cachedMetadata;
        }

        if (IsTemporarilyBlocked())
        {
            return null;
        }

        if (!TryGetBaseUri(out var baseUri))
        {
            return null;
        }

        await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (TryGetCachedMetadata(normalizedId, out cachedMetadata))
            {
                return cachedMetadata;
            }

            if (IsTemporarilyBlocked())
            {
                return null;
            }

            var delay = _nextRequestAt - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }

            _nextRequestAt = DateTimeOffset.UtcNow.AddMilliseconds(
                Math.Clamp(
                    _pluginConfiguration.Current
                        .JavDbRequestDelayMilliseconds,
                    0,
                    60000));

            var requestUri = new Uri(
                baseUri,
                $"/search?f=all&locale=en&q={Uri.EscapeDataString(normalizedId)}");
            var result = await FetchMetadataAsync(
                    requestUri,
                    normalizedId,
                    baseUri,
                    cancellationToken)
                .ConfigureAwait(false);
            if (result.Cacheable)
            {
                _cache[normalizedId] = new CachedMetadata(
                    result.Metadata,
                    DateTimeOffset.UtcNow.Add(
                        result.Metadata is null
                            ? NotFoundCacheLifetime
                            : CacheLifetime));
            }

            return result.Metadata;
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private async Task<FetchResult> FetchMetadataAsync(
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
                "Mozilla/5.0 (X11; Linux x86_64) "
                + "AppleWebKit/537.36 (KHTML, like Gecko) "
                + "Chrome/137.0.0.0 Safari/537.36");
            request.Headers.Accept.ParseAdd(
                "text/html,application/xhtml+xml,application/xml;q=0.9,"
                + "image/avif,image/webp,*/*;q=0.8");
            request.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
            request.Headers.Referrer = baseUri;
            request.Headers.TryAddWithoutValidation(
                "Upgrade-Insecure-Requests",
                "1");
            using var response = await client
                .SendAsync(request, cancellationToken)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new FetchResult(null, true);
            }

            if (response.StatusCode is HttpStatusCode.Forbidden
                or HttpStatusCode.TooManyRequests)
            {
                var cooldown = GetAccessBlockedCooldown(response);
                _blockedUntil = DateTimeOffset.UtcNow.Add(cooldown);
                _logger.LogWarning(
                    "JavDB returned HTTP {StatusCode} for {MovieId}; pausing JavDB requests for {DelayMinutes:F0} minutes.",
                    (int)response.StatusCode,
                    expectedId,
                    cooldown.TotalMinutes);
                return new FetchResult(null, false);
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "JavDB returned HTTP {StatusCode} for {MovieId}.",
                    (int)response.StatusCode,
                    expectedId);
                return new FetchResult(null, false);
            }

            var html = await response.Content
                .ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);
            var document = await new HtmlParser()
                .ParseDocumentAsync(html, cancellationToken)
                .ConfigureAwait(false);
            if (IsAccessBlocked(document))
            {
                _blockedUntil = DateTimeOffset.UtcNow.Add(
                    AccessBlockedCooldown);
                _logger.LogWarning(
                    "JavDB did not expose public search results for {MovieId}; pausing JavDB requests for {DelayMinutes:F0} minutes.",
                    expectedId,
                    AccessBlockedCooldown.TotalMinutes);
                return new FetchResult(null, false);
            }

            _blockedUntil = DateTimeOffset.MinValue;
            return new FetchResult(
                ParseMetadata(document, expectedId, baseUri),
                true);
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(
                exception,
                "Could not retrieve JavDB metadata for {MovieId}.",
                expectedId);
            return new FetchResult(null, false);
        }
    }

    private MovieMetadata? ParseMetadata(
        IDocument document,
        string expectedId,
        Uri baseUri)
    {
        foreach (var item in document.QuerySelectorAll(".movie-list .item"))
        {
            var actualId = item.QuerySelector(".video-title strong")
                ?.TextContent
                .Trim();
            if (!IdsEqual(actualId, expectedId))
            {
                continue;
            }

            var anchor = item.QuerySelector("a.box");
            var title = anchor?.GetAttribute("title")?.Trim();
            if (string.IsNullOrWhiteSpace(title))
            {
                title = GetTitleFromCard(item, actualId!);
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                return null;
            }

            actualId = CatalogueIdFormatter.Format(actualId);
            var releaseDateText = item.QuerySelector(".meta")
                ?.TextContent
                .Trim();
            var releaseDate = DateTime.TryParseExact(
                releaseDateText,
                ReleaseDateFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsedDate)
                ? parsedDate
                : (DateTime?)null;
            var imageUrl = GetAbsoluteHttpUrl(
                baseUri,
                item.QuerySelector(".cover img")?.GetAttribute("src"));

            return new MovieMetadata
            {
                Name = title,
                CatalogueId = actualId,
                OriginalTitle = title,
                ReleaseDate = releaseDate,
                Tags = CatalogueIdTags.Create(actualId),
                ProviderIds = new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    [JavDbProvider.Key] = actualId!
                },
                PrimaryImage = imageUrl is null
                    ? null
                    : new RemoteImageMetadata
                    {
                        Url = imageUrl
                    }
            };
        }

        return null;
    }

    private static bool IsAccessBlocked(IDocument document)
    {
        return document.QuerySelector("form[action='/user_sessions']") is not null
            || document.QuerySelector("script[src*='challenge-platform']") is not null;
    }

    private bool IsTemporarilyBlocked()
    {
        return _blockedUntil > DateTimeOffset.UtcNow;
    }

    private static TimeSpan GetAccessBlockedCooldown(
        HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return delta;
        }

        if (retryAfter?.Date is { } date)
        {
            var delay = date - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                return delay;
            }
        }

        return AccessBlockedCooldown;
    }

    private bool TryGetCachedMetadata(
        string normalizedId,
        out MovieMetadata? metadata)
    {
        if (_cache.TryGetValue(normalizedId, out var cached)
            && cached.ExpiresAt > DateTimeOffset.UtcNow)
        {
            metadata = cached.Metadata;
            return true;
        }

        _cache.TryRemove(normalizedId, out _);
        metadata = null;
        return false;
    }

    private bool TryGetBaseUri(out Uri baseUri)
    {
        var value = _pluginConfiguration.Current.JavDbBaseUrl;
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

    private static string? GetTitleFromCard(IElement item, string id)
    {
        var text = item.QuerySelector(".video-title")?.TextContent.Trim();
        return text?.StartsWith(id, StringComparison.OrdinalIgnoreCase) == true
            ? text[id.Length..].Trim()
            : text;
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
        return CatalogueIdFormatter.Format(id) ?? string.Empty;
    }

    private static bool IdsEqual(string? left, string right)
    {
        return string.Equals(
            NormalizeId(left ?? string.Empty),
            NormalizeId(right),
            StringComparison.OrdinalIgnoreCase);
    }

    private sealed record CachedMetadata(
        MovieMetadata? Metadata,
        DateTimeOffset ExpiresAt);

    private sealed record FetchResult(
        MovieMetadata? Metadata,
        bool Cacheable);
}
