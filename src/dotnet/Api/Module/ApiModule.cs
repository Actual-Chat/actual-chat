using ActualChat.Contacts;
using ActualChat.MediaPlayback;

namespace ActualChat.Module;

/// <summary>
/// Registers Api project services with the DI container.
/// </summary>

public sealed class ApiModule(IServiceProvider moduleServices)
    : HostModule(moduleServices)
{
    protected override void InjectServices(IServiceCollection services)
    {
        // Common services
        var fusion = services.AddFusion();
        services.AddSingleton(c => new UrlMapper(c.HostInfo()));

        // MarkupParser
        var rawParser = new MarkupParser();
        if (HostInfo.HostKind.IsServer()) {
            var sharedCache = new ConcurrentLruCache<string, Markup>(16384, HardwareInfo.GetProcessorCountPo2Factor(4));
            var sharedParser = new CachingMarkupParser(rawParser, sharedCache);
            services.AddSingleton(sharedParser);
            services.AddSingleton<IMarkupParser>(_ => {
                var scopedCache = new ThreadSafeLruCache<string, Markup>(256);
                var scopedParser = new CachingMarkupParser(sharedParser, scopedCache);
                return scopedParser;
            });
        }
        else { // WASM and MAUI apps
            var sharedCache = new ThreadSafeLruCache<string, Markup>(4096);
            var sharedParser = new CachingMarkupParser(rawParser, sharedCache);
            services.AddSingleton(sharedParser);
            services.AddScoped<IMarkupParser>(_ => sharedParser);
        }

        if (HostInfo.HostKind.HasBlazorUI()) {
            // MediaPlayback
            services.AddScoped<IPlaybackFactory>(c => new PlaybackFactory(c));
            fusion.AddService<ActivePlaybackInfo>(ServiceLifetime.Scoped);
        }

        services.AddSingleton<ExternalContactHasher>();
    }
}
