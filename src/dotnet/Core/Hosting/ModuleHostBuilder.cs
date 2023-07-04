namespace ActualChat.Hosting;

public readonly record struct ModuleHostBuilder(ImmutableList<HostModule> Modules)
{
    public ModuleHostBuilder() : this(ImmutableList<HostModule>.Empty) { }

    public ModuleHostBuilder WithModule(HostModule module)
        => new(Modules.Add(module));

    public ModuleHostBuilder WithModules(params HostModule[] modules)
    {
        var newModules = Modules;
        foreach (var module in modules)
            newModules = newModules.Add(module);
        return new(newModules);
    }

    public ModuleHost Build(IServiceCollection services)
    {
        var host = new ModuleHost(Modules.ToArray());
        services.AddSingleton(host);

        // 1. Initialize / bind modules to ModuleHost
        foreach (var module in host.Modules)
            module.Initialize(host);

        // 2. Let modules to inject their services
        foreach (var module in host.Modules)
            module.InjectServices(services);

        return host;
    }
}
