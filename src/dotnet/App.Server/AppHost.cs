using ActualChat.App.Server.Initializers;
using ActualChat.Mesh;

namespace ActualChat.App.Server;

public class AppHost : IDisposable
{
    public static readonly string DefaultServerUrls = "http://localhost:7080";

    private volatile int _isDisposed;

    public string ServerUrls { get; set; } = DefaultServerUrls;
    public WebApplicationOptions HostOptions { get; set; } = new();
    public Action<AppHostBuilder, IConfigurationManager>? Configure { get; set; }
    public Action<AppHostBuilder, IServiceCollection>? ConfigureModuleHostServices { get; set; }
    public Action<AppHostBuilder, IServiceCollection>? ConfigureServices { get; set; }
    public Action<AppHostBuilder, WebApplication>? ConfigureApp { get; set; }

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

    public virtual AppHost Build(bool configurationOnly = false)
    {
        App = new AppHostBuilder(this, configurationOnly).App;
        return this;
    }

    public virtual async Task InvokeInitializers(CancellationToken cancellationToken = default)
        => await InvokeInitializers([
                new ExecuteDbInitializers(Services),
                new ExecuteModuleInitializers(Services),
            ],
            cancellationToken
        )
        .ConfigureAwait(false);

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
