using System.Diagnostics.CodeAnalysis;
using ActualChat.Hosting;
using Stl.Plugins;

namespace ActualChat.Chat.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public class ChatModule : HostModule
{
    public ChatModule(IPluginInfoProvider.Query _) : base(_) { }

    [ServiceConstructor]
    public ChatModule(IPluginHost plugins) : base(plugins) { }
    public override void InjectServices(IServiceCollection services)
    {
        var rawParser = new MarkupParser();
        if (HostInfo.RequiredServiceScopes.Contains(ServiceScope.Server)) {
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
