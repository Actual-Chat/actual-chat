using System;
using ActualChat.Hosting;
using ActualChat.UI.Blazor;
using Stl.DependencyInjection;
using Stl.Plugins;

namespace ActualChat.Todos.UI.Blazor
{
    public class TodosBlazorUIModule : HostModule, IBlazorUIModule
    {
        public TodosBlazorUIModule(IPluginInfoProvider.Query _) : base(_) { }
        [ServiceConstructor]
        public TodosBlazorUIModule(IServiceProvider services) : base(services) { }
    }
}
