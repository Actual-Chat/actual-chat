using ActualLab.Fusion.UI;
using ActualLab.Internal;

namespace ActualChat.App.Maui.IosShareExt.UI.Fusion.Ios;

public interface IStatefulView : IHasServices
{
    State State { get; }
    void DisposeStates();
}

public interface IStatefulView<T> : IStatefulView
{
    new IState<T> State { get; }
}

public abstract class StatefulView : UIView, IStatefulView, IEnumerable<UIView>
{
    private int _isDisposed;

    protected IosHub Hub { get; }
    public IServiceProvider Services => Hub.Services;
    protected UICommander UICommander => Hub.UICommander;
    protected Session Session => Hub.Session;
    protected State State { get; private set; } = null!;
    protected Action<State, StateEventKind> StateChanged { get; set; }
    protected ILogger Log => field ??= Hub.LogFor(GetType());
    public bool IsDisposed => Volatile.Read(ref _isDisposed) != 0;

    State IStatefulView.State => State;

    protected StatefulView(IosHub hub)
    {
        Hub = hub;
        StateChanged = (_, _) => {
            if (State is IHasDisposeStatus { IsDisposed: true })
                return;

            // Same as ExecutionContextExt.TrySuppressFlow(), but a bit faster
            if (ExecutionContext.IsFlowSuppressed())
                NotifyStateHasChanged();
            else {
                using var _ = ExecutionContext.SuppressFlow();
                NotifyStateHasChanged();
            }
        };
        EnsureStateIsCreated();
    }

    public void DisposeStates()
    {
        // Drops the states without touching the native view, which Dispose is free to do only
        // once UIKit is done with the view - a disposed managed peer that still gets a callback
        // (ContactListView is a live IUICollectionViewDelegate) takes the extension down.
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
            return;

        DisposeStatesCore();
    }

    protected override void Dispose(bool disposing)
    {
        // A native dispose has to take the states with it, or a State outlives its view and
        // keeps recomputing (and keeps its scoped services alive). Never on the finalizer
        // path: touching other managed objects there isn't safe.
        if (disposing)
            DisposeStates();

        base.Dispose(disposing);
    }

    // Override to add cleanup of your own; DisposeStates is what keeps this to a single run
    protected virtual void DisposeStatesCore()
    {
        foreach (var subview in Subviews)
            subview.DisposeStates();
        if (State is IDisposable state)
            state.DisposeSilently();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected void EnsureStateIsCreated()
    {
        if (!ReferenceEquals(State, null))
            return;

        var (state, stateOptions) = CreateState();
        SetState(state, stateOptions);
    }

    protected void SetState(
        IState state,
        StateEventKind stateChangedEventKind = StateEventKind.Updated)
        => SetState((State)state, null, stateChangedEventKind);

    protected void SetState(
        IState state,
        object? stateInitializeOptions,
        StateEventKind stateChangedEventKind = StateEventKind.Updated)
        => SetState((State)state, stateInitializeOptions, stateChangedEventKind);

    protected void SetState(
        State state,
        StateEventKind stateChangedEventKind = StateEventKind.Updated)
        => SetState(state, null, stateChangedEventKind);

    protected virtual void SetState(
        State state,
        object? stateInitializeOptions,
        StateEventKind stateChangedEventKind = StateEventKind.Updated)
    {
        if (!ReferenceEquals(State, null))
            throw Errors.AlreadyInitialized(nameof(State));

        State = state ?? throw new ArgumentNullException(nameof(state));
        state.AddEventHandler(stateChangedEventKind, StateChanged);
        if (stateInitializeOptions is not null && state is IHasInitialize hasInitialize)
            hasInitialize.Initialize(stateInitializeOptions);
    }

    protected abstract (State State, object? StateInitializeOptions) CreateState();

    protected abstract void NotifyStateHasChanged();

    protected EventHandler Safe(Action action)
        // Deliberately not UICommander + LocalCommand: ICommander.Run wraps every command into
        // Task.Run, and these actions read UIKit properties of the control that raised the event,
        // which is main-thread only. Nothing reads UIActionFailureTracker here either, so the
        // error would land nowhere instead of in the log.
        => (_, _) => {
            try {
                action();
            }
            catch (Exception e) {
                Log.LogError(e, "Failed to call event handler");
            }
        };

    protected EventHandler Safe(Func<CancellationToken, Task> action)
        => (sender, args) => {
            _ = SafeAsync();

            async Task SafeAsync()
            {
                try {
                    await action(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception e) {
                    Log.LogError(e, "Failed to call async event handler");
                }
            }
        };

    public new IEnumerator<UIView> GetEnumerator()
        => ((IEnumerable<UIView>)Subviews).GetEnumerator();
}

public abstract class StatefulView<T>(IosHub hub) : StatefulView(hub), IStatefulView<T>
{
    protected State UntypedState => base.State;
    protected new IState<T> State => Unsafe.As<IState<T>>(base.State);
    IState<T> IStatefulView<T>.State => Unsafe.As<IState<T>>(base.State);
}
