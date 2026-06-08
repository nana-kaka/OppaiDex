using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.OppaiDex.Providers;

public sealed class R18ImageProvider : IRemoteImageProvider
{
    private readonly ILogger<R18ImageProvider> _logger;

    public R18ImageProvider(ILogger<R18ImageProvider> logger)
    {
        _logger = logger;
    }

    public string Name => ProviderNames.R18DisplayName;

    public bool Supports(BaseItem item)
    {
        return item is Movie;
    }

    public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
    {
        return [ImageType.Primary];
    }

    public async Task<IEnumerable<RemoteImageInfo>> GetImages(
        BaseItem item,
        CancellationToken cancellationToken)
    {
        if (!item.ProviderIds.TryGetValue(ProviderNames.R18, out var id))
        {
            return [];
        }

        var movie = await R18MovieProvider
            .GetMovieAsync(id, _logger, cancellationToken)
            .ConfigureAwait(false);
        var imageUrl = movie?.JacketFullUrl ?? movie?.JacketThumbUrl;

        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            return [];
        }

        return
        [
            new RemoteImageInfo
            {
                ProviderName = Name,
                Url = imageUrl,
                ThumbnailUrl = movie?.JacketThumbUrl,
                Type = ImageType.Primary
            }
        ];
    }

    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        return Plugin.Instance.GetHttpClient().GetAsync(url, cancellationToken);
    }
}
