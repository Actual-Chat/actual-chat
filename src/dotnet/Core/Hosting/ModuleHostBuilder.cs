namespace ActualChat.Hosting;

public class ModuleHostBuilder
{
    public List<HostModule> Modules { get; } = new();

    public ModuleHostBuilder AddModule(HostModule module)
    {
        Modules.Add(module);
        return this;
    }

    public ModuleHostBuilder AddModules(params HostModule[] modules)
    {
        Modules.AddRange(modules);
        return this;
    }

    public ModuleHost Build(IServiceCollection services)
    {
        var host = new ModuleHost(Modules.ToArray());
        services.AddSingleton(host);

        // 1. Initialize / bind modules to ModuleHost
        foreach (var module in host.Modules)
            module.Initialize(host, services);

        // 2. Let modules to inject their services
        foreach (var module in host.Modules)
            if (module.IsUsed)
                module.InjectServices(services);

        return host;
    }
}
