using System.Diagnostics.CodeAnalysis;
using ActualChat.Hosting;

namespace ActualChat.Chat.Module;

#pragma warning disable IL2026 // Fine for modules

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class ChatModule(IServiceProvider moduleServices) : HostModule(moduleServices)
{
    protected override void InjectServices(IServiceCollection services)
    {
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
    }
}
