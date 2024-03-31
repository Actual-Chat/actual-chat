using ActualChat.App.Server.Initializers;
using ActualChat.Mesh;
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
        if (disposing)
            App.DisposeSilently();
    }

    public virtual async Task InvokeInitializers(CancellationToken cancellationToken = default)
    {
        var initializers = new IAggregateInitializer[] {
            new ExecuteDbInitializers(Services),
            new ExecuteModuleInitializers(Services),
        };
        await InvokeInitializers(initializers, cancellationToken).ConfigureAwait(false);

        // NOTE(AY):
        // Since InvokeInitializers is called before App.Run(), the host isn't listening yet.
        // So if every available host is in this state, none of them is listening.
        // And if all of them use a backend service running in Hybrid or Client mode,
        // they'll try to connect to corresponding peers, which will take indefinitely long,
        // since all of them are still initializing (and listening yet).
        // See e.g. UsersDbInitializer.EnsureAdminExists - apparently, it's going to resort to
        // an RPC call in Hybrid or Client mode, so the initialization will stuck right there.
        var rpcBackendDelegates = Services.GetRequiredService<RpcBackendDelegates>();
        rpcBackendDelegates.StartRouting();
    }

    public virtual Task Run(CancellationToken cancellationToken = default)
        => App.RunAsync(cancellationToken);

    public virtual Task Start(CancellationToken cancellationToken = default)
        => App.StartAsync(cancellationToken);

    public virtual Task Stop(CancellationToken cancellationToken = default)
        => App.StopAsync(cancellationToken);

    // Private methods

    private async Task InvokeInitializers(IEnumerable<IAggregateInitializer> initializers, CancellationToken cancellationToken = default)
    {
#if DEBUG
        // See MeshLockBase.DefaultLockOptions - locks expire much longer in DEBUG
        var mustLock = false;
#else
        var hostInfo = Services.HostInfo();
        var mustLock = hostInfo.IsTested;
#endif

        var tasks = initializers.Select(initializer => mustLock
            ? InvokeInitializersProtected(initializer, cancellationToken)
            : InvokeInitializersUnsafe(initializer, cancellationToken));

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task InvokeInitializersProtected(IAggregateInitializer initializer, CancellationToken cancellationToken = default)
    {
        var meshLocks = Services.MeshLocks<InfrastructureDbContext>().WithKeyPrefix(nameof(AppHost));
        var lockKey = initializer.GetType().Name;
        const string lockValue = nameof(InvokeInitializersProtected);
        var lockHolder = await meshLocks.Lock(lockKey, lockValue, cancellationToken).ConfigureAwait(false);
        await using var _ = lockHolder.ConfigureAwait(false);
        var lockCts = cancellationToken.LinkWith(lockHolder.StopToken);

        await InvokeInitializersUnsafe(initializer, lockCts.Token).ConfigureAwait(false);
    }

    private static async Task InvokeInitializersUnsafe(IAggregateInitializer initializer, CancellationToken cancellationToken)
        => await initializer
            .InvokeAll(cancellationToken)
            .ConfigureAwait(false);
}
