using Microsoft.Extensions.Configuration;

namespace ActualChat.Hosting;

/// <summary>
/// Marker interface for server-side modules.
/// </summary>
public interface IServerModule;

/// <summary>
/// Marker interface for client-side app modules.
/// </summary>
public interface IAppModule;

/// <summary>
/// Interface for Blazor UI modules that require JavaScript imports.
/// </summary>
public interface IBlazorUIModule
{
    public static abstract string ImportName { get; }
}

/// <summary>
/// Base class for modules that register services with a <see cref="ModuleHost"/>.
/// </summary>
public abstract class HostModule(IServiceProvider moduleServices)
{
    public IServiceProvider ModuleServices { get; } = moduleServices;
    public HostInfo HostInfo => field ??= ModuleServices.HostInfo();
    public IConfiguration Cfg => field ??= ModuleServices.Configuration();
    public ILogger Log => field ??= ModuleServices.LogFor(GetType());

    public ModuleHost Host { get; private set; } = null!;
    public bool IsUsed { get; protected set; } = true;

    protected internal virtual void Initialize(ModuleHost host, IServiceCollection services)
    {
        if (this is IServerModule && !HostInfo.HostKind.IsServer())
            throw StandardError.Internal("This module can be used only on the server side.");
        if (this is IAppModule && !HostInfo.HostKind.IsApp())
            throw StandardError.Internal("This module can be used only in apps.");
        if (this is IBlazorUIModule && !HostInfo.HasRole(HostRole.BlazorHost))
            IsUsed = false;

        Host = host;
    }

    protected internal abstract void InjectServices(IServiceCollection services);
}

/// <summary>
/// Base class for modules with strongly-typed configuration settings.
/// </summary>
public abstract class HostModule<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TSettings>(
    IServiceProvider moduleServices
    ) : HostModule(moduleServices)
    where TSettings : class, new()
{
    public TSettings Settings => field ??= LoadSettings();

    protected virtual TSettings LoadSettings()
        => Cfg.Settings<TSettings>();

    protected internal override void Initialize(ModuleHost host, IServiceCollection services)
    {
        base.Initialize(host, services);
        if (IsUsed)
            services.AddSingleton(Settings);
    }
}
