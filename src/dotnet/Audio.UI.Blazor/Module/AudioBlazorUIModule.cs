using ActualChat.Hosting;
using ActualChat.UI.Blazor;
using Microsoft.Extensions.DependencyInjection;
using Stl.DependencyInjection;
using Stl.Plugins;

namespace ActualChat.Audio.UI.Blazor
{
    public class AudioBlazorUIModule : HostModule, IBlazorUIModule
    {
        /// <inheritdoc />
        public static string ImportName => "audio";

        public AudioBlazorUIModule(IPluginInfoProvider.Query _) : base(_) { }
        [ServiceConstructor]
        public AudioBlazorUIModule(IPluginHost plugins) : base(plugins) { }

        public override void InjectServices(IServiceCollection services)
        {
            if (!HostInfo.RequiredServiceScopes.Contains(ServiceScope.BlazorUI))
                return; // Blazor UI only module
        }
    }
}
