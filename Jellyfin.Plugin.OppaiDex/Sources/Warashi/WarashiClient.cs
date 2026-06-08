using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Jellyfin.Plugin.OppaiDex.Configuration;
using MediaBrowser.Common.Net;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.OppaiDex.Sources.Warashi;

public sealed class WarashiClient
{
    private static readonly Regex ProfileIdRegex = new(
        @"/en/(?<section>s-\d+-\d+)/[^/]+/[^/]+/(?<id>\d+)(?:$|[?#])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WarashiClient> _logger;
    private readonly PluginConfigurationAccessor _pluginConfiguration;

    public WarashiClient(
        IHttpClientFactory httpClientFactory,
        ILogger<WarashiClient> logger,
        PluginConfigurationAccessor pluginConfiguration)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _pluginConfiguration = pluginConfiguration;
    }

    public async Task<IReadOnlyList<WarashiSearchResult>> SearchAsync(
        string name,
        CancellationToken cancellationToken)
    {
        if (!TryGetBaseUri(out var baseUri) || string.IsNullOrWhiteSpace(name))
        {
            return [];
        }

        using var content = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("recherche_critere", "f"),
            new KeyValuePair<string, string>("recherche_valeur", name),
            new KeyValuePair<string, string>("x", "0"),
            new KeyValuePair<string, string>("y", "0")
        ]);

        var document = await SendAsync(
                HttpMethod.Post,
                new Uri(baseUri, "/en/s-12/search"),
                content,
                cancellationToken)
            .ConfigureAwait(false);
        if (document is null)
        {
            return [];
        }

        return document
            .QuerySelectorAll(".resultat-pornostar")
            .Select(row => ParseSearchResult(row, baseUri))
            .Where(result => result is not null)
            .Cast<WarashiSearchResult>()
            .ToArray();
    }

    public async Task<WarashiPerson?> FindExactAsync(
        string name,
        CancellationToken cancellationToken)
    {
        var normalizedName = NormalizeName(name);
        var results = await SearchAsync(name, cancellationToken)
            .ConfigureAwait(false);
        var match = results.FirstOrDefault(
            result => NormalizeName(result.Name) == normalizedName
                || result.Aliases.Any(
                    alias => NormalizeName(alias) == normalizedName));

        return match is null
            ? null
            : await GetPersonAsync(match.Id, cancellationToken)
                .ConfigureAwait(false);
    }

    public async Task<WarashiPerson?> GetPersonAsync(
        string id,
        CancellationToken cancellationToken)
    {
        if (!TryGetBaseUri(out var baseUri) || !TryParseId(id, out var section, out var numericId))
        {
            return null;
        }

        var uri = new Uri(
            baseUri,
            $"/en/{section}/profile/asian-pornstar/{numericId}");
        var document = await SendAsync(
                HttpMethod.Get,
                uri,
                null,
                cancellationToken)
            .ConfigureAwait(false);
        if (document is null)
        {
            return null;
        }

        var name = CleanText(
            document.QuerySelector("#pornostar-profil [itemprop=name]")
                ?.TextContent);
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var birthDateText = document
            .QuerySelector("[itemprop=birthDate]")
            ?.GetAttribute("content");
        DateTime? birthDate = DateTime.TryParseExact(
            birthDateText,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsedBirthDate)
            ? parsedBirthDate
            : null;

        var aliases = document
            .QuerySelectorAll("#pornostar-profil-noms-alternatifs li")
            .Select(element => CleanText(element.TextContent))
            .Where(alias => !string.IsNullOrWhiteSpace(alias))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var images = document
            .QuerySelectorAll("#pornostar-profil-photos img")
            .Select(image =>
                image.ParentElement?.LocalName == "a"
                    ? image.ParentElement.GetAttribute("href")
                    : image.GetAttribute("src"))
            .Select(path => GetAbsoluteHttpUrl(baseUri, path))
            .Where(url => url is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new WarashiPerson
        {
            Id = $"{section}/{numericId}",
            Name = name,
            BirthDate = birthDate,
            BirthPlace = CleanText(
                document.QuerySelector("[itemprop=birthPlace]")?.TextContent),
            Aliases = aliases,
            ImageUrls = images
        };
    }

    private async Task<IDocument?> SendAsync(
        HttpMethod method,
        Uri uri,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(method, uri)
            {
                Content = content
            };
            using var client =
                _httpClientFactory.CreateClient(NamedClient.Default);
            using var response = await client
                .SendAsync(request, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "WAPdB returned HTTP {StatusCode} for {Url}.",
                    (int)response.StatusCode,
                    uri);
                return null;
            }

            var html = await response.Content
                .ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);
            return await new HtmlParser()
                .ParseDocumentAsync(html, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(exception, "Could not retrieve WAPdB page {Url}.", uri);
            return null;
        }
    }

    private static WarashiSearchResult? ParseSearchResult(
        IElement row,
        Uri baseUri)
    {
        var link = row.QuerySelector("a");
        var path = link?.GetAttribute("href");
        if (!TryExtractId(path, out var id))
        {
            return null;
        }

        var name = CleanText(row.QuerySelector("p a")?.TextContent);
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return new WarashiSearchResult
        {
            Id = id,
            Name = name.Split('-', 2)[0].Trim(),
            ImageUrl = GetAbsoluteHttpUrl(
                baseUri,
                row.QuerySelector("img")?.GetAttribute("src")),
            Aliases = row
                .QuerySelectorAll("p:last-child span")
                .Select(element => CleanText(element.TextContent))
                .Where(alias => !string.IsNullOrWhiteSpace(alias))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    private bool TryGetBaseUri(out Uri baseUri)
    {
        var value = _pluginConfiguration.Current.WarashiBaseUrl;
        if (_pluginConfiguration.Current.WarashiEnabled
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

    private static bool TryExtractId(string? path, out string id)
    {
        var match = ProfileIdRegex.Match(path ?? string.Empty);
        if (match.Success)
        {
            id = $"{match.Groups["section"].Value}/{match.Groups["id"].Value}";
            return true;
        }

        id = string.Empty;
        return false;
    }

    private static bool TryParseId(
        string id,
        out string section,
        out string numericId)
    {
        var parts = id.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2
            && parts[0].StartsWith("s-", StringComparison.Ordinal)
            && parts[1].All(char.IsDigit))
        {
            section = parts[0];
            numericId = parts[1];
            return true;
        }

        section = string.Empty;
        numericId = string.Empty;
        return false;
    }

    private static string? GetAbsoluteHttpUrl(Uri baseUri, string? path)
    {
        if (!Uri.TryCreate(baseUri, path, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp
                && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        return uri.ToString();
    }

    private static string NormalizeName(string name)
    {
        return string.Concat(
            name.Where(char.IsLetterOrDigit))
            .ToUpperInvariant();
    }

    private static string? CleanText(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : string.Join(
                ' ',
                value.Split(
                    (char[]?)null,
                    StringSplitOptions.RemoveEmptyEntries));
    }
}
