using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.OppaiDex.Sources.Warashi;

public sealed class WarashiPersonProvider :
    IRemoteMetadataProvider<Person, PersonLookupInfo>
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly WarashiClient _warashiClient;

    public WarashiPersonProvider(
        WarashiClient warashiClient,
        IHttpClientFactory httpClientFactory)
    {
        _warashiClient = warashiClient;
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
        WarashiPerson? person;
        if (info.ProviderIds.TryGetValue(WarashiProvider.Key, out var id))
        {
            person = await _warashiClient
                .GetPersonAsync(id, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            person = await _warashiClient
                .FindExactAsync(info.Name, cancellationToken)
                .ConfigureAwait(false);
        }

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

    public Task<HttpResponseMessage> GetImageResponse(
        string url,
        CancellationToken cancellationToken)
    {
        return _httpClientFactory
            .CreateClient(NamedClient.Default)
            .GetAsync(url, cancellationToken);
    }
}
