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
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.OppaiDex.Providers;

public sealed class OppaiDexImageProvider : IRemoteImageProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly MetadataSourceRegistry _sourceRegistry;

    public OppaiDexImageProvider(
        MetadataSourceRegistry sourceRegistry,
        IHttpClientFactory httpClientFactory)
    {
        _sourceRegistry = sourceRegistry;
        _httpClientFactory = httpClientFactory;
    }

    public string Name => Constants.PluginName;

    public bool Supports(BaseItem item)
    {
        return item is Movie;
    }

    public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
    {
        return [ImageType.Primary, ImageType.Backdrop];
    }

    public async Task<IEnumerable<RemoteImageInfo>> GetImages(
        BaseItem item,
        CancellationToken cancellationToken)
    {
        foreach (var source in _sourceRegistry.GetCandidates(item.ProviderIds))
        {
            var id = source.GetLookupId(item.ProviderIds, item.Path, item.Name);
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var metadata = await source
                .GetMetadataAsync(id, cancellationToken)
                .ConfigureAwait(false);
            if (metadata is not null)
            {
                return CreateImages(source.DisplayName, metadata);
            }
        }

        return [];
    }

    public Task<HttpResponseMessage> GetImageResponse(
        string url,
        CancellationToken cancellationToken)
    {
        return _httpClientFactory
            .CreateClient(NamedClient.Default)
            .GetAsync(url, cancellationToken);
    }

    private static IEnumerable<RemoteImageInfo> CreateImages(
        string providerName,
        MovieMetadata metadata)
    {
        var images = new List<RemoteImageInfo>();

        if (metadata.PrimaryImage is not null)
        {
            images.Add(new RemoteImageInfo
            {
                ProviderName = providerName,
                Url = metadata.PrimaryImage.Url,
                ThumbnailUrl = metadata.PrimaryImage.ThumbnailUrl,
                Type = ImageType.Primary
            });
        }

        images.AddRange(metadata.Backdrops.Select(image => new RemoteImageInfo
        {
            ProviderName = providerName,
            Url = image.Url,
            ThumbnailUrl = image.ThumbnailUrl,
            Type = ImageType.Backdrop
        }));

        return images;
    }
}
