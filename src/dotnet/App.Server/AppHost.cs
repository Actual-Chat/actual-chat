using ActualChat.App.Server.Initializers;
using ActualChat.Rpc.Internal;

namespace ActualChat.App.Server;

public partial class AppHost : IDisposable
{
    public static readonly string DefaultServerUrls = "http://localhost:7080";

    private volatile int _isDisposed;

    public string ServerUrls { get; set; } = DefaultServerUrls;
    public WebApplicationOptions HostOptions { get; set; } = new();
    public Action<IConfigureHostContext, IConfigurationManager>? ConfigureHost { get; set; }
    public Action<IConfigureModuleServicesContext, IServiceCollection>? ConfigureModuleServices { get; set; }
    public Action<IConfigureServicesContext, IServiceCollection>? ConfigureServices { get; set; }
    public Action<IConfigureAppContext, WebApplication>? ConfigureApp { get; set; }

    public WebApplication App { get; protected set; } = null!;
    public IServiceProvider Services => App.Services;

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _isDisposed, 1, 0) != 0)
            return;

        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
            return;

        var disposeTask = BackgroundTask.Run(async () => {
            try {
                await App.StopAsync(CancellationToken.None).SilentAwait(false);
            }
            catch {
                // Intended
            }
            await App.DisposeSilentlyAsync().SilentAwait(false);
        }, CancellationToken.None);
#pragma warning disable VSTHRD002
        disposeTask.Wait();
#pragma warning restore VSTHRD002
    }

    public async Task RunInitializers(CancellationToken cancellationToken = default)
    {
        var meshLocks = Services.MeshLocks<InfrastructureDbContext>().WithKeyPrefix($"{nameof(AppHost)}");
        var initializers = new WorkerBase[] {
            new AggregateDbInitializer(Services),
            new AggregateModuleInitializer(Services),
        };
        await meshLocks
            .LockAndRun(nameof(RunInitializers), RunInitializersImpl, cancellationToken)
            .ConfigureAwait(false);

        // NOTE(AY):
        // Since InvokeInitializers is called before App.Run(), the host isn't listening yet.
        // So if every available host is in this state, none of them is listening.
        // And if all of them use a backend service running in Hybrid or Client mode,
        // they'll try to connect to corresponding peers, which will take indefinitely long,
        // since all of them are still initializing (and listening yet).
        // See e.g. UsersDbInitializer.EnsureAdminExists - apparently, it's going to resort to
        // an RPC call in Hybrid or Client mode, so the initialization will stuck right there.
        var rpcBackendHelpers = Services.GetRequiredService<RpcBackendHelpers>();
        rpcBackendHelpers.StartRouting();
        return;

        Task RunInitializersImpl(CancellationToken ct)
            => Task.WhenAll(initializers.Select(x => x.Run(ct)));
    }

    public Task Run(CancellationToken cancellationToken = default)
        => App.RunAsync(cancellationToken);

    public Task Start(CancellationToken cancellationToken = default)
        => App.StartAsync(cancellationToken);

    public Task Stop(CancellationToken cancellationToken = default)
        => App.StopAsync(cancellationToken);
}
