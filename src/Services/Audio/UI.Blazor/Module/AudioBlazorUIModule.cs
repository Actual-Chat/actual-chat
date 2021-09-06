using System;
using System.Collections.Generic;
using System.Linq;
using ActualChat.Hosting;
using ActualChat.Streaming.Client.Module;
using ActualChat.UI.Blazor;
using Microsoft.Extensions.DependencyInjection;
using Stl.DependencyInjection;
using Stl.Plugins;

namespace ActualChat.Audio.UI.Blazor.Module
{
    public class AudioBlazorUIModule: HostModule, IBlazorUIModule
    {
        public AudioBlazorUIModule(IPluginInfoProvider.Query _) : base(_) { }
        [ServiceConstructor]
        public AudioBlazorUIModule(IPluginHost plugins) : base(plugins) { }

        public override IEnumerable<Type> Dependencies =>
            base.Dependencies.Concat(new[] { typeof(StreamingClientModule) });

        public override void InjectServices(IServiceCollection services)
        {
            if (!HostInfo.RequiredServiceScopes.Contains(ServiceScope.BlazorUI))
                return; // Blazor UI only module
        }
    }
}
