using System;
using Microsoft.Extensions.DependencyInjection;

namespace ActualChat.Todos
{
    public class TodosClientModule : Module
    {
        public TodosClientModule(IServiceCollection services, IServiceProvider moduleBuilderServices)
            : base(services, moduleBuilderServices) { }
    }
}
