using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.OppaiDex.Sources.JavDb;

public sealed class JavDbExternalId : IExternalId
{
    public string ProviderName => JavDbProvider.DisplayName;

    public string Key => JavDbProvider.Key;

    public ExternalIdMediaType? Type => ExternalIdMediaType.Movie;

    public bool Supports(IHasProviderIds item)
    {
        return item is Movie;
    }
}
