using ActualChat.Hosting;

namespace ActualChat.DependencyInjection;

public sealed class Scope : IAsyncDisposable, IHasServices, IHasIsDisposed
{
    private readonly List<Task> _tasks = new();
    private readonly CancellationTokenSource _whenStoppedCts;
    private Task? _whenStopped;
    private Session? _session;

    public IServiceProvider Services { get; }
    public HostInfo HostInfo { get; }
    public IStateFactory StateFactory { get; }
    public ILoggerFactory LoggerFactory { get; }
    public MomentClockSet Clocks { get; }
    public Session Session => _session ??= Services.GetRequiredService<Session>();

    public CancellationToken StopToken { get; }
    public Task WhenStopped => _whenStopped ?? GetWhenDisposed();
    public bool IsDisposed => StopToken.IsCancellationRequested;

    public Scope(IServiceProvider services)
    {
        Services = services;
        HostInfo = services.GetRequiredService<HostInfo>();
        StateFactory = services.GetRequiredService<IStateFactory>();
        LoggerFactory = services.GetRequiredService<ILoggerFactory>();
        Clocks = services.GetRequiredService<MomentClockSet>();

        _whenStoppedCts = new CancellationTokenSource();
        StopToken = _whenStoppedCts.Token;
    }

    public ValueTask DisposeAsync()
    {
        lock (_tasks)
            _whenStoppedCts.CancelAndDisposeSilently();
        return Task.WhenAll(_tasks).ToValueTask();
    }

    public void Register(Task task)
    {
        lock (_tasks) {
            StopToken.ThrowIfCancellationRequested();
            _tasks.Add(task);
        }
    }

    // Private methods

    private Task GetWhenDisposed()
    {
        lock (_tasks) {
            if (_whenStopped != null)
                return _whenStopped;
            if (_whenStoppedCts.IsCancellationRequested)
                return _whenStopped = Task.FromCanceled(StopToken);

            var tcs = new TaskCompletionSource();
            StopToken.Register(() => tcs.TrySetCanceled(StopToken));
            return _whenStopped = tcs.Task;
        }
    }
}
