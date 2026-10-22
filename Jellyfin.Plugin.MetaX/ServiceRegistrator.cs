using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MetaX;

public sealed class ServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton(provider =>
        {
            var handler = new SocketsHttpHandler
            {
                ConnectTimeout = TimeSpan.FromSeconds(30),
                KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always,
                KeepAlivePingDelay = TimeSpan.FromSeconds(30),
                KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                PooledConnectionIdleTimeout = TimeSpan.FromSeconds(90)
            };

            return new MetaXApiClient(
                () => Plugin.Instance.Configuration,
                new HttpClient(handler),
                provider.GetRequiredService<ILogger<MetaXApiClient>>());
        });

        serviceCollection.AddSingleton(provider => new ImageProxyService(
            provider.GetRequiredService<IDataProtectionProvider>(),
            () => applicationHost.GetLocalApiUrl("127.0.0.1", "http", null)));

        serviceCollection.AddSingleton(provider => new LibraryCategoryResolver(
            provider.GetRequiredService<MediaBrowser.Controller.Library.ILibraryManager>(),
            () => Plugin.Instance.Configuration,
            provider.GetRequiredService<ILogger<LibraryCategoryResolver>>()));
    }
}
