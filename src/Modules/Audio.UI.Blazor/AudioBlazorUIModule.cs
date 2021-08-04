using ActualChat.Hosting;
using ActualChat.UI.Blazor;
using Stl.DependencyInjection;
using Stl.Plugins;

namespace ActualChat.Voice.UI.Blazor
{
    public class AudioBlazorUIModule: HostModule, IBlazorUIModule
    {
        public AudioBlazorUIModule(IPluginInfoProvider.Query _) : base(_) { }
        [ServiceConstructor]
        public AudioBlazorUIModule(IPluginHost plugins) : base(plugins) { }
    }
}