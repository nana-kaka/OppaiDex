namespace Jellyfin.Plugin.OppaiDex.Configuration;

public class PluginConfigurationAccessor
{
    public virtual PluginConfiguration Current => Plugin.Instance.Configuration;
}
