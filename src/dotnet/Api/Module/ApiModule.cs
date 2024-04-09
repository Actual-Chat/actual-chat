using System.Diagnostics.CodeAnalysis;
using ActualChat.Chat;
using ActualChat.Contacts;
using ActualChat.Hosting;
using ActualChat.MediaPlayback;

namespace ActualChat.Module;

#pragma warning disable IL2026 // Fine for modules

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class ApiModule(IServiceProvider moduleServices)
    : HostModule(moduleServices)
{
    protected override void InjectServices(IServiceCollection services)
    {
        var fusion = services.AddFusion();

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
