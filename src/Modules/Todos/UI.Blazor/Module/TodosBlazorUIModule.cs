using System;
using ActualChat.Hosting;
using ActualChat.UI.Blazor;
using Stl.DependencyInjection;
using Stl.Plugins;

namespace ActualChat.Todos.UI.Blazor.Module
{
    public class TodosBlazorUIModule : HostModule, IBlazorUIModule
    {
        public TodosBlazorUIModule(IPluginInfoProvider.Query _) : base(_) { }
        [ServiceConstructor]
        public TodosBlazorUIModule(IPluginHost plugins) : base(plugins) { }
    }
}
