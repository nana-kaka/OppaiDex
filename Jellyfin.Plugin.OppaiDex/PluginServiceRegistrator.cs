using System;
using System.Linq;
using Jellyfin.Plugin.OppaiDex.Configuration;
using Jellyfin.Plugin.OppaiDex.Metadata;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.OppaiDex;

public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(
        IServiceCollection serviceCollection,
        IServerApplicationHost applicationHost)
    {
        var sourceTypes = typeof(PluginServiceRegistrator).Assembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false }
                && typeof(IMovieMetadataSource).IsAssignableFrom(type));

        foreach (var sourceType in sourceTypes)
        {
            serviceCollection.AddSingleton(typeof(IMovieMetadataSource), sourceType);
        }

        serviceCollection.AddSingleton<PluginConfigurationAccessor>();
        serviceCollection.AddSingleton<MetadataSourceRegistry>();
    }
}
