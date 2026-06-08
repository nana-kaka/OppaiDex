using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.OppaiDex.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        ApiUrlTemplate = "https://r18.dev/videos/vod/movies/detail/-/combined={id}/json";
    }

    /// <summary>
    /// Gets or sets the API URL template. The movie identifier replaces the {id} placeholder.
    /// </summary>
    public string ApiUrlTemplate { get; set; }
}
