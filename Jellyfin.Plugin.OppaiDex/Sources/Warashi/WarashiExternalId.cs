using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.OppaiDex.Sources.Warashi;

public sealed class WarashiExternalId : IExternalId
{
    public string ProviderName => WarashiProvider.DisplayName;

    public string Key => WarashiProvider.Key;

    public ExternalIdMediaType? Type => ExternalIdMediaType.Person;

    public bool Supports(IHasProviderIds item)
    {
        return item is Person;
    }
}
