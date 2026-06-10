using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.OppaiDex.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.OppaiDex.Sources.Warashi;

public sealed class WarashiPersonProvider :
    IRemoteMetadataProvider<Person, PersonLookupInfo>
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PluginConfigurationAccessor _pluginConfiguration;
    private readonly WarashiClient _warashiClient;

    public WarashiPersonProvider(
        WarashiClient warashiClient,
        PluginConfigurationAccessor pluginConfiguration,
        IHttpClientFactory httpClientFactory)
    {
        _warashiClient = warashiClient;
        _pluginConfiguration = pluginConfiguration;
        _httpClientFactory = httpClientFactory;
    }

    public string Name => WarashiProvider.DisplayName;

    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(
        PersonLookupInfo searchInfo,
        CancellationToken cancellationToken)
    {
        var results = await _warashiClient
            .SearchAsync(searchInfo.Name, cancellationToken)
            .ConfigureAwait(false);

        return results.Select(result => new RemoteSearchResult
        {
            Name = result.Name,
            ImageUrl = result.ImageUrl,
            ProviderIds = new Dictionary<string, string>
            {
                [WarashiProvider.Key] = result.Id
            }
        });
    }

    public async Task<MetadataResult<Person>> GetMetadata(
        PersonLookupInfo info,
        CancellationToken cancellationToken)
    {
        var person = info.ProviderIds.TryGetValue(WarashiProvider.Key, out var id)
            ? await _warashiClient
                .GetPersonAsync(id, cancellationToken)
                .ConfigureAwait(false)
            : await _warashiClient
                .FindExactAsync(info.Name, cancellationToken)
                .ConfigureAwait(false);

        if (person is null)
        {
            return new MetadataResult<Person>();
        }

        return new MetadataResult<Person>
        {
            HasMetadata = true,
            Item = new Person
            {
                Name = info.Name,
                Overview = _pluginConfiguration.Current
                    .WarashiBiographyProfileEnabled
                    ? CreateProfileOverview(person)
                    : null,
                PremiereDate = person.BirthDate,
                ProductionLocations = string.IsNullOrWhiteSpace(person.BirthPlace)
                    ? []
                    : [person.BirthPlace],
                ProviderIds = new Dictionary<string, string>
                {
                    [WarashiProvider.Key] = person.Id
                }
            }
        };
    }

    private static string? CreateProfileOverview(WarashiPerson person)
    {
        var facts = new[]
        {
            CreateProfileFact("Measurements", person.Measurements),
            CreateProfileFact("Cup size", person.CupSize),
            CreateProfileFact("Height", person.Height),
            CreateProfileFact("Weight", person.Weight),
            CreateProfileFact("Blood type", person.BloodType)
        }
            .Where(fact => fact is not null)
            .Cast<string>()
            .ToArray();
        if (facts.Length == 0)
        {
            return null;
        }

        var overview = new StringBuilder("Profile");
        overview.AppendLine();
        overview.AppendLine();
        overview.AppendJoin(
            string.Concat(Environment.NewLine, Environment.NewLine),
            facts);
        return overview.ToString();
    }

    private static string? CreateProfileFact(string label, string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : $"{label}: {value}";
    }

    public Task<HttpResponseMessage> GetImageResponse(
        string url,
        CancellationToken cancellationToken)
    {
        return _httpClientFactory
            .CreateClient(NamedClient.Default)
            .GetAsync(url, cancellationToken);
    }
}
