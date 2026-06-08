namespace Jellyfin.Plugin.OppaiDex.Configuration;

public sealed class PluginConfigurationAccessor
{
    public PluginConfiguration Current => Plugin.Instance.Configuration;
}
