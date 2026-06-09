using System;
using System.ComponentModel;
using System.Text.Json.Serialization;
using System.Xml.Serialization;
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
        R18ApiUrlTemplate = Constants.DefaultR18ApiUrlTemplate;
        R18RequestDelayMilliseconds =
            Constants.DefaultR18RequestDelayMilliseconds;
        WarashiBaseUrl = Constants.DefaultWarashiBaseUrl;
        WarashiEnabled = true;
    }

    /// <summary>
    /// Gets or sets the R18.dev API URL template.
    /// The movie identifier replaces the {id} placeholder.
    /// </summary>
    public string R18ApiUrlTemplate { get; set; }

    /// <summary>
    /// Gets or sets the minimum delay between R18.dev requests.
    /// </summary>
    public int R18RequestDelayMilliseconds { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether WAPdB person metadata is enabled.
    /// </summary>
    public bool WarashiEnabled { get; set; }

    /// <summary>
    /// Gets or sets the WAPdB base URL.
    /// </summary>
    public string WarashiBaseUrl { get; set; }

    /// <summary>
    /// Gets or sets the legacy R18.dev API URL template.
    /// </summary>
    [Obsolete("Use R18ApiUrlTemplate instead.")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [JsonIgnore]
    [XmlIgnore]
    public string ApiUrlTemplate
    {
        get => R18ApiUrlTemplate;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                R18ApiUrlTemplate = value;
            }
        }
    }
}
