using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.OppaiDex.Sources.Warashi;

public sealed class WarashiPersonImageProvider : IRemoteImageProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly WarashiClient _warashiClient;

    public WarashiPersonImageProvider(
        WarashiClient warashiClient,
        IHttpClientFactory httpClientFactory)
    {
        _warashiClient = warashiClient;
        _httpClientFactory = httpClientFactory;
    }

    public string Name => WarashiProvider.DisplayName;

    public bool Supports(BaseItem item)
    {
        return item is Person;
    }

    public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
    {
        return [ImageType.Primary];
    }

    public async Task<IEnumerable<RemoteImageInfo>> GetImages(
        BaseItem item,
        CancellationToken cancellationToken)
    {
        if (!item.ProviderIds.TryGetValue(WarashiProvider.Key, out var id))
        {
            return [];
        }

        var person = await _warashiClient
            .GetPersonAsync(id, cancellationToken)
            .ConfigureAwait(false);
        if (person is null)
        {
            return [];
        }

        return person.ImageUrls.Select(url => new RemoteImageInfo
        {
            ProviderName = Name,
            Type = ImageType.Primary,
            Url = url
        });
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
