using Jellyfin.Plugin.ThemeStore.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.ThemeStore
{
    public class PluginServiceRegistrar : IPluginServiceRegistrator
    {
        public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
        {
            serviceCollection.AddSingleton<ThemeCatalogService>();
            serviceCollection.AddSingleton<UserThemeStore>();
            serviceCollection.AddHostedService<FileTransformationRegistrar>();
        }
    }
}
