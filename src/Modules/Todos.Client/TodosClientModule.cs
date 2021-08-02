using System;
using ActualChat.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Stl.DependencyInjection;
using Stl.Plugins;

namespace ActualChat.Todos.Client
{
    public class TodosClientModule : HostModule
    {
        public TodosClientModule(IPluginInfoProvider.Query _) : base(_) { }
        [ServiceConstructor]
        public TodosClientModule(IServiceProvider services) : base(services) { }
    }
}
