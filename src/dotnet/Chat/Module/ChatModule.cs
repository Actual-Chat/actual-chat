using ActualChat.Hosting;
using Stl.OS;
using Stl.Plugins;

namespace ActualChat.Chat.Module;

public class ChatModule : HostModule
{
    public ChatModule(IPluginInfoProvider.Query _) : base(_) { }

    [ServiceConstructor]
    public ChatModule(IPluginHost plugins) : base(plugins) { }
    public override void InjectServices(IServiceCollection services)
    {
        if (HostInfo.HostKind == HostKind.WebServer) {
            var rawParser = new MarkupParser();
            var sharedCache = new ConcurrentLruCache<string, Markup>(65536, HardwareInfo.GetProcessorCountPo2Factor(4));
            var sharedParser = new CachingMarkupParser(rawParser, sharedCache);
            services.AddSingleton(sharedParser);
            services.AddScoped<IMarkupParser>(c => {
                var scopedCache = new ThreadSafeLruCache<string, Markup>(256);
                var scopedParser = new CachingMarkupParser(sharedParser, scopedCache);
                return scopedParser;
            });
        }
        else { // WASM host and MAUI host
            var rawParser = new MarkupParser();
            var sharedCache = new ThreadSafeLruCache<string, Markup>(4096);
            var sharedParser = new CachingMarkupParser(rawParser, sharedCache);
            services.AddSingleton(sharedParser);
            services.AddScoped<IMarkupParser>(_ => sharedParser);
        }
    }
}
