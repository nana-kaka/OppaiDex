using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.OppaiDex.Sources.JavDatabase;

public sealed class JavDatabaseExternalId : IExternalId
{
    public string ProviderName => JavDatabaseProvider.DisplayName;

    public string Key => JavDatabaseProvider.Key;

    public ExternalIdMediaType? Type => ExternalIdMediaType.Movie;

    public bool Supports(IHasProviderIds item)
    {
        return item is Movie;
    }
}
