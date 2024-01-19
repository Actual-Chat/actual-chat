using ActualChat.Hosting;
using ActualChat.Kvas;
using ActualLab.Rpc;

namespace ActualChat.DependencyInjection;

public abstract class Hub : IServiceProvider, IHasServices, IAsyncDisposable, IHasIsDisposed
{
    private readonly HostInfo _hostInfo;
    private readonly IStateFactory _stateFactory;
    private readonly ILoggerFactory _logs;
    private readonly MomentClockSet _clocks;
    private readonly List<Task> _tasks = new();
    private readonly List<object> _disposables = new();
    private readonly CancellationTokenSource _whenStoppedCts;
    private volatile Task? _whenDisposed;
    private Session? _session;
    private UrlMapper? _urlMapper;
    private AccountSettings? _accountSettings;
    private LocalSettings? _localSettings;
    private Features? _features;
    private ICommander? _commander;
    private RpcHub? _rpcHub;

    public IServiceProvider Services { get; }

    // These properties are exposed as methods to "close" the static ones on IServiceProvider
    public HostInfo HostInfo() => _hostInfo;
    public Session Session() => _session ??= Services.GetRequiredService<Session>();
    public UrlMapper UrlMapper() => _urlMapper ??= Services.GetRequiredService<UrlMapper>();
    public AccountSettings AccountSettings() => _accountSettings ??= Services.GetRequiredService<AccountSettings>();
    public LocalSettings LocalSettings() => _localSettings ??= Services.GetRequiredService<LocalSettings>();
    public Features Features() => _features ??= Services.GetRequiredService<Features>();
    public ICommander Commander() => _commander ??= Services.GetRequiredService<ICommander>();
    public RpcHub RpcHub() => _rpcHub ??= Services.GetRequiredService<RpcHub>();

    public CancellationToken StopToken { get; }
    public Task? WhenDisposed => _whenDisposed;
    public bool IsDisposed => StopToken.IsCancellationRequested;

    protected Hub(IServiceProvider services)
    {
        Services = services;
        _hostInfo = services.HostInfo();
        _stateFactory = services.GetRequiredService<IStateFactory>();
        _logs = services.GetRequiredService<ILoggerFactory>();
        _clocks = services.GetRequiredService<MomentClockSet>();

        _whenStoppedCts = new CancellationTokenSource();
        StopToken = _whenStoppedCts.Token;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IStateFactory StateFactory() => _stateFactory;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ILoggerFactory Logs() => _logs;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MomentClockSet Clocks() => _clocks;

    public ValueTask DisposeAsync()
    {
        lock (_tasks) {
            if (_whenDisposed == null) {
                _whenStoppedCts.CancelAndDisposeSilently();
                _whenDisposed = DisposeAsyncCore();
            }
            return _whenDisposed.ToValueTask();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public object? GetService(Type serviceType)
        => Services.GetService(serviceType);

    public ILogger<T> LogFor<T>()
        => Logs().CreateLogger<T>();
    public ILogger LogFor(Type type)
        => Logs().CreateLogger(type.NonProxyType());
    public ILogger LogFor(string category)
        => Logs().CreateLogger(category);

    public void RegisterAwaitable(Task task)
    {
        lock (_tasks) {
            StopToken.ThrowIfCancellationRequested();
            _tasks.Add(task);
        }
    }

    public void RegisterDisposable(object disposableOrAction)
    {
        var isDisposed = false;
        lock (_tasks) {
            if (IsDisposed)
                isDisposed = true;
            else
                _disposables.Add(disposableOrAction);
        }
        if (isDisposed)
            _ = DisposableExt.DisposeUnknownSilently(disposableOrAction);
    }

    // Private methods

    private async Task DisposeAsyncCore() {
        await Task.WhenAll(_tasks).SilentAwait(false);
        for (var i = _disposables.Count - 1; i >= 0; i--)
            await Dispose(_disposables[i]).SilentAwait();
    }

    private static ValueTask Dispose(object? disposableOrAction)
    {
        switch (disposableOrAction) {
        case IAsyncDisposable ad:
            return ad.DisposeSilentlyAsync();
        case IDisposable d:
            d.DisposeSilently();
            return default;
        case Func<ValueTask> f:
            return f.Invoke();
        case Action a:
            a.Invoke();
            return default;
        }
        return default;
    }
}
