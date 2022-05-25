using System.Reactive.Linq;

namespace ActualChat.UI.Blazor.Services;

public interface IPersistentState<T> : IMutableState<T>, IAsyncDisposable
{ }
public class PersistentState<T>: MutableState<T>, IPersistentState<T>
{
    private readonly object _lock = new ();
    private readonly CancellationTokenSource _cts;
    private IDisposable? _updatedObservableCompletion;
    private Task _lastPersistTask;

    protected Func<T, ISessionCommand> PersistCommandFactory { get; }
    protected UICommandRunner UICommandRunner { get; }

    public new record Options : MutableState<T>.Options
    {
        public TimeSpan SampleInterval { get; init; } = TimeSpan.FromMilliseconds(1000);
        public Options()
            => ComputedOptions = ComputedOptions.NoAutoInvalidateOnError;
    }

    protected internal PersistentState(
        Options options,
        Func<T, ISessionCommand> persistCommandFactory,
        IServiceProvider services)
        : base(options, services, true)
    {
        PersistCommandFactory = persistCommandFactory;
        UICommandRunner = services.GetRequiredService<UICommandRunner>();
        _cts = new CancellationTokenSource();
        _lastPersistTask = Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _updatedObservableCompletion.DisposeSilently();
        await _lastPersistTask.ConfigureAwait(false);
    }

    protected override void Initialize(State<T>.Options options)
    {
        base.Initialize(options);
        var updatedObservable = Observable.FromEvent<Action<IState<T>, StateEventKind>, (IState<T>, StateEventKind)>(
            onNext => (st, kind) => onNext((st, kind)),
            h => Updated += h,
            h => Updated -= h);
        _updatedObservableCompletion = updatedObservable
            .Sample((options as Options)?.SampleInterval ?? TimeSpan.FromMilliseconds(1000))
            .Subscribe(OnUpdated);
    }

    private void OnUpdated((IState<T> State, StateEventKind EventKind) tuple)
    {
        var (_, eventKind) = tuple;
        if ((eventKind & StateEventKind.Updated) == 0 || !HasValue)
            return;

        lock (_lock)
            _lastPersistTask = WaitAndPersist(_lastPersistTask, Value);

        async Task WaitAndPersist(Task prevTask, T value)
        {
            await prevTask.ConfigureAwait(false);
            var command = PersistCommandFactory(value);
            await UICommandRunner.Run(command, _cts.Token).ConfigureAwait(false);
        }
    }
}
