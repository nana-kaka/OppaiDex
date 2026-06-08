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
using Jellyfin.Plugin.OppaiDex.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.OppaiDex.Providers;

public sealed partial class R18MovieProvider : IRemoteMetadataProvider<Movie, MovieInfo>, IHasOrder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ILogger<R18MovieProvider> _logger;

    public R18MovieProvider(ILogger<R18MovieProvider> logger)
    {
        _logger = logger;
    }

    public string Name => ProviderNames.R18DisplayName;

    public int Order => 0;

    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(
        MovieInfo searchInfo,
        CancellationToken cancellationToken)
    {
        var id = GetMovieId(searchInfo);
        var movie = await GetMovieAsync(id, _logger, cancellationToken).ConfigureAwait(false);
        if (movie is null)
        {
            return [];
        }

        return
        [
            new RemoteSearchResult
            {
                Name = GetTitle(movie),
                ImageUrl = movie.JacketThumbUrl ?? movie.JacketFullUrl,
                Overview = movie.CommentEnglish,
                ProductionYear = movie.ReleaseDate?.Year,
                ProviderIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [ProviderNames.R18] = movie.DvdId ?? movie.ContentId ?? id!
                }
            }
        ];
    }

    public async Task<MetadataResult<Movie>> GetMetadata(
        MovieInfo info,
        CancellationToken cancellationToken)
    {
        var id = GetMovieId(info);
        var movie = await GetMovieAsync(id, _logger, cancellationToken).ConfigureAwait(false);
        if (movie is null)
        {
            return new MetadataResult<Movie>();
        }

        var item = new Movie
        {
            Name = GetTitle(movie),
            OriginalTitle = movie.TitleEnglish,
            Overview = movie.CommentEnglish,
            PremiereDate = movie.ReleaseDate,
            ProductionYear = movie.ReleaseDate?.Year,
            RunTimeTicks = movie.RuntimeMinutes is > 0
                ? TimeSpan.FromMinutes(movie.RuntimeMinutes.Value).Ticks
                : null,
            Genres = movie.Categories
                .Select(category => category.DisplayName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Studios = GetStudios(movie),
            ProviderIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ProviderNames.R18] = movie.DvdId ?? movie.ContentId ?? id!
            }
        };

        var result = new MetadataResult<Movie>
        {
            HasMetadata = true,
            Item = item
        };

        AddPeople(result, movie.Actresses.Concat(movie.Actors).Concat(movie.Histrions), PersonKind.Actor);
        AddPeople(result, movie.Directors, PersonKind.Director);
        AddPeople(result, movie.Authors, PersonKind.Writer);

        return result;
    }

    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        return Plugin.Instance.GetHttpClient().GetAsync(url, cancellationToken);
    }

    internal static async Task<R18Movie?> GetMovieAsync(
        string? id,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var template = Plugin.Instance.Configuration.ApiUrlTemplate;
        if (string.IsNullOrWhiteSpace(template) || !template.Contains("{id}", StringComparison.Ordinal))
        {
            logger.LogError("The OppaiDex API URL template must contain '{{id}}'.");
            return null;
        }

        var normalizedId = NormalizeId(id);
        var url = template.Replace(
            "{id}",
            Uri.EscapeDataString(normalizedId),
            StringComparison.Ordinal);

        try
        {
            using var response = await Plugin.Instance.GetHttpClient()
                .GetAsync(url, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
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
            logger.LogError(exception, "Could not retrieve metadata for {MovieId}.", normalizedId);
            return null;
        }
        catch (JsonException exception)
        {
            logger.LogError(exception, "Could not parse metadata for {MovieId}.", normalizedId);
            return null;
        }
    }

    private static string? GetMovieId(MovieInfo info)
    {
        if (info.ProviderIds.TryGetValue(ProviderNames.R18, out var providerId)
            && !string.IsNullOrWhiteSpace(providerId))
        {
            return providerId;
        }

        foreach (var candidate in new[] { info.Path, info.Name })
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            var fileName = Path.GetFileNameWithoutExtension(candidate);
            var match = MovieIdRegex().Match(fileName);
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

    private static string NormalizeId(string id)
    {
        return string.Concat(id.Where(char.IsLetterOrDigit)).ToLower(CultureInfo.InvariantCulture);
    }

    private static string GetTitle(R18Movie movie)
    {
        return movie.TitleEnglishUncensored
            ?? movie.TitleEnglish
            ?? movie.DvdId
            ?? movie.ContentId
            ?? "Unknown";
    }

    private static string[] GetStudios(R18Movie movie)
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

    private static void AddPeople(
        MetadataResult<Movie> result,
        IEnumerable<R18Person> people,
        PersonKind kind)
    {
        foreach (var name in people
                     .Select(person => person.DisplayName)
                     .Where(name => !string.IsNullOrWhiteSpace(name))
                     .Cast<string>()
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            result.AddPerson(new PersonInfo
            {
                Name = name,
                Type = kind
            });
        }
    }

    [GeneratedRegex(
        @"(?<![A-Za-z0-9])(?<prefix>[A-Za-z]{2,12})[\s._-]?(?<number>\d{2,6})(?!\d)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MovieIdRegex();
}
