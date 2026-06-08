using System;
using System.Linq;
using Jellyfin.Plugin.OppaiDex.Configuration;
using Jellyfin.Plugin.OppaiDex.Metadata;
using Jellyfin.Plugin.OppaiDex.Sources.Warashi;
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

        var personEnricherTypes = typeof(PluginServiceRegistrator).Assembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false }
                && typeof(IPersonMetadataEnricher).IsAssignableFrom(type));

        foreach (var enricherType in personEnricherTypes)
        {
            serviceCollection.AddSingleton(
                typeof(IPersonMetadataEnricher),
                enricherType);
        }

        serviceCollection.AddSingleton<PluginConfigurationAccessor>();
        serviceCollection.AddSingleton<WarashiClient>();
        serviceCollection.AddSingleton<MetadataSourceRegistry>();
    }
}
