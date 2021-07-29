using System;
using Castle.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Stl.DependencyInjection;
using Stl.Extensibility;

namespace ActualChat
{
    [RegisterModule]
    public abstract class Module : ModuleBase
    {
        public IServiceProvider ModuleBuilderServices { get; }
        public HostInfo HostInfo { get; }

        public Module(IServiceCollection services, IServiceProvider moduleBuilderServices) : base(services)
        {
            ModuleBuilderServices = moduleBuilderServices;
            HostInfo = moduleBuilderServices.GetRequiredService<HostInfo>();
        }

        public override void Use()
        {
            var moduleAssembly = GetType().Assembly;
            var scanner = Services.UseRegisterAttributeScanner();
            scanner = scanner.RegisterFrom(moduleAssembly); // Add shared services
            if (!HostInfo.ServiceScope.IsEmpty) // Add host-specific services
                scanner = scanner
                    .WithScope(HostInfo.ServiceScope)
                    .RegisterFrom(moduleAssembly);
        }
    }
}
