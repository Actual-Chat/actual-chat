using Stl.Extensibility;

namespace ActualChat.Hosting;

public sealed class ModuleHostBuilder
{
    private readonly ImmutableStack<HostModule> _modules;

    public ModuleHostBuilder()
        => _modules = ImmutableStack<HostModule>.Empty;

    private ModuleHostBuilder(ModuleHostBuilder builder, params HostModule[] modules)
    {
        _modules = builder._modules;
        foreach (var module in modules)
            _modules = _modules.Push(module);
    }

    public ModuleHostBuilder AddModule(params HostModule[] modules) => new (this, modules);

    public ModuleHost Build(IServiceCollection services)
    {
        var host = new ModuleHost(_modules.Reverse().ToArray());
        services.AddSingleton(host);

        foreach (var module in host.GetModules())
            module.InjectServices(services);
        services.AddSingleton<IMatchingTypeFinder>(c => new StaticMatchingTypeFinder(c));

        return host;
    }
}
