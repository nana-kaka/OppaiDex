using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.OppaiDex.Sources.R18;

public sealed class R18ExternalId : IExternalId
{
    public string ProviderName => R18Provider.DisplayName;

    public string Key => R18Provider.Key;

    public ExternalIdMediaType? Type => null;

    public bool Supports(IHasProviderIds item)
    {
        return item is Movie or Person;
    }
}

public sealed class R18ContentExternalId : IExternalId
{
    public string ProviderName => "R18.dev Content ID";

    public string Key => R18Provider.ContentIdKey;

    public ExternalIdMediaType? Type => ExternalIdMediaType.Movie;

    public bool Supports(IHasProviderIds item)
    {
        return item is Movie;
    }
}
