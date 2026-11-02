using Jellyfin.Plugin.RecentlyAddedBadges.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.RecentlyAddedBadges;

/// <summary>Registers request filters and web-shell injection.</summary>
public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<RecentlyAddedBadgeFilter>();
        serviceCollection.AddSingleton<IStartupFilter, WebScriptStartupFilter>();
        serviceCollection.Configure<MvcOptions>(options =>
        {
            options.Filters.AddService<RecentlyAddedBadgeFilter>();
        });
    }
}
