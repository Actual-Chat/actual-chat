using System;
using Microsoft.Extensions.DependencyInjection;
using Stl.DependencyInjection;
using Stl.Plugins;

namespace ActualChat.Hosting
{
    public abstract class HostModule : Plugin
    {
        protected HostInfo HostInfo { get; } = null!;

        protected HostModule(IPluginInfoProvider.Query _) : base(_) { }
        protected HostModule(IServiceProvider services) : base(services)
            => HostInfo = Services.GetRequiredService<HostInfo>();

        public virtual void InjectServices(IServiceCollection services)
        {
            var moduleAssembly = GetType().Assembly;
            var scanner = services.UseRegisterAttributeScanner();
            scanner = scanner.RegisterFrom(moduleAssembly); // Add shared services
            if (!HostInfo.ServiceScope.IsEmpty) // Add host-specific services
                scanner = scanner
                    .WithScope(HostInfo.ServiceScope)
                    .RegisterFrom(moduleAssembly);
        }
    }
}
