using ActualChat.Hosting;
using ActualChat.UI.Blazor;
using Stl.DependencyInjection;
using Stl.Plugins;

namespace ActualChat.Voice.UI.Blazor
{
    public class VoiceBlazorUIModule: HostModule, IBlazorUIModule
    {
        public VoiceBlazorUIModule(IPluginInfoProvider.Query _) : base(_) { }
        [ServiceConstructor]
        public VoiceBlazorUIModule(IPluginHost plugins) : base(plugins) { }
    }
}