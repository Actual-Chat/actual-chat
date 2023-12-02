using ActualChat.Hosting;

namespace ActualChat.DependencyInjection;

public sealed class Scope : IAsyncDisposable, IHasServices, IHasIsDisposed
{
    private readonly IStateFactory _stateFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly MomentClockSet _clocks;
    private readonly List<Task> _tasks = new();
    private readonly List<object> _disposables = new();
    private readonly CancellationTokenSource _whenStoppedCts;
    private volatile Task? _whenDisposed;
    private Session? _session;

    public IServiceProvider Services { get; }
    public HostInfo HostInfo { get; }
    public Session Session => _session ??= Services.GetRequiredService<Session>();

    public CancellationToken StopToken { get; }
    public Task? WhenDisposed => _whenDisposed;
    public bool IsDisposed => StopToken.IsCancellationRequested;

    public Scope(IServiceProvider services)
    {
        Services = services;
        HostInfo = services.GetRequiredService<HostInfo>();
        _stateFactory = services.GetRequiredService<IStateFactory>();
        _loggerFactory = services.GetRequiredService<ILoggerFactory>();
        _clocks = services.GetRequiredService<MomentClockSet>();

        _whenStoppedCts = new CancellationTokenSource();
        StopToken = _whenStoppedCts.Token;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IStateFactory StateFactory() => _stateFactory;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ILoggerFactory LoggerFactory() => _loggerFactory;
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
