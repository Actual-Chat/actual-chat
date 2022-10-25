using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

// Keeps track of the initial location.
// Service should be instantiated on app start.
public class HistoryUI
{
    private readonly List<HistoryItem> _history = new ();
    private readonly Task _whenJsRefInitialized;
    private PendingHistoryItem? _pendingHistoryItem;
    private IJSObjectReference? _jsRef;
    private Task _whenLocationChangedHandled;
    private State _state;
    private int _historyIndex;

    private BrowserInfo BrowserInfo { get; }
    private IJSRuntime JS { get; }
    private ILogger<HistoryUI> Log { get; }
    private NavigationManager Nav { get; }

    public string InitialLocation { get; }
    public bool IsInitialLocation { get; private set; }
    public Task WhenInitialized { get; }

    public event EventHandler<EventArgs>? AfterLocationChangedHandled;

    public HistoryUI(
        BrowserInfo browserInfo,
        IJSRuntime js,
        NavigationManager nav,
        ILogger<HistoryUI> log)
    {
        BrowserInfo = browserInfo;
        JS = js;
        Log = log;
        Nav = nav;

        InitialLocation = Nav.Uri;
        IsInitialLocation = true;
        _historyIndex = 0;
        _history.Add(new HistoryItem(InitialLocation));
        Log.LogDebug("Initial location: '{Location}'", Nav.Uri);

        // HistoryUI is initialized upon BlazorCircuitContext is created.
        // At this moment there is no yet subscribers to Nav.LocationChanged.
        // So HistoryUI will be notified the first on location changed, even before the Router.
        Nav.LocationChanged += OnLocationChanged;

        _whenLocationChangedHandled = Task.CompletedTask;
        _whenJsRefInitialized = InitializeJsRef();
        _state = new State { Index = 0 };
        WhenInitialized = InitializeState(_state);
    }

    public async Task RouterOnNavigateAsync(NavigationContext arg)
        // Postpone Router navigation till location changed async completed.
        // We can get rid of this event handler
        // after OnLocationChangedAsync is replaced with synchronous implementation in .NET 7.
        => await _whenLocationChangedHandled;

    public Task GoBack()
        => JS.InvokeVoidAsync("eval", "history.back()").AsTask();

    public void NavigateTo(Action? onForwardAction, Action? onBackAction)
    {
        if (_pendingHistoryItem != null)
            throw StandardError.Constraint("There is still pending history item");
        var historyItem = new HistoryItem(Nav.Uri) {
            OnForwardAction = onForwardAction,
            OnBackAction = onBackAction
        };
        _pendingHistoryItem = new PendingHistoryItem(historyItem, _historyIndex);
        Nav.NavigateTo(Nav.Uri);
    }

    private async Task<State?> GetStateAsync()
    {
        await _whenJsRefInitialized.ConfigureAwait(false);
        var result = await _jsRef!.InvokeAsync<State?>("getState").ConfigureAwait(false);
        return result;
    }

    private async Task InitializeJsRef()
        => _jsRef = await JS.InvokeAsync<IJSObjectReference>(
            $"{BlazorUICoreModule.ImportName}.HistoryUI.create")
            .ConfigureAwait(false);

    private async Task InitializeState(State state)
    {
        await _whenJsRefInitialized;
        await SetStateAsync(state);
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        IsInitialLocation = false;

        var location = e.Location;
        Log.LogDebug("Location changed: '{Location}'", location);

        if (!BrowserInfo.ScreenSize.Value.IsNarrow())
            return;

        var tcs = new TaskCompletionSource();
        _whenLocationChangedHandled = tcs.Task;

        _ = OnLocationChangedAsync(tcs, e.Location);
    }

    private async Task OnLocationChangedAsync(TaskCompletionSource whenCompleted, string location)
    {
        try {
            var previousState = _state;
            HistoryMove move;
            // TODO: in .NET 7 NavigationManager provides access to state property of the history API.
            // Rework history position tracking with it.
            var readState = await GetStateAsync();
            if (readState == null) {
                // State is null, apparently it's an internal navigation to next location happened.
                // Lets store this in the state.
                var state = new State { Index = previousState.Index + 1 };
                await SetStateAsync(state);
                _state = state;
                move = HistoryMove.Navigate;
            }
            else {
                move = readState.Index < previousState.Index
                    ? HistoryMove.Backward
                    : readState.Index > previousState.Index
                        ? HistoryMove.Forward
                        : throw StandardError.Constraint("Invalid state index for history move.");
                _state = readState;
            }

            HistoryItem historyItem;
            if (move == HistoryMove.Navigate) {
                if (_pendingHistoryItem != null) {
                    if (_pendingHistoryItem.HistoryIndex != _historyIndex)
                        throw StandardError.Constraint("PendingHistoryItem is not consistent.");
                    historyItem = _pendingHistoryItem.HistoryItem;
                    _pendingHistoryItem = null;
                }
                else
                    historyItem = new HistoryItem(location);
                _historyIndex++;
                if (_history.Count < _historyIndex)
                    throw StandardError.Constraint("History is not consistent.");
                if (_history.Count > _historyIndex)
                    _history.RemoveRange(_historyIndex, _history.Count - _historyIndex);
                _history.Add(historyItem);
            }
            else if (move == HistoryMove.Forward) {
                _historyIndex = readState!.Index;
                if (_history.Count < _historyIndex)
                    throw StandardError.Constraint("History does not contain forward item.");
                historyItem = _history[_historyIndex];
            } else {
                _historyIndex = readState!.Index;
                if (_history.Count < _historyIndex + 1)
                    throw StandardError.Constraint("History does not contain backward item.");
                historyItem = _history[_historyIndex + 1];
            }

            if (move != HistoryMove.Backward)
                historyItem.OnForwardAction?.Invoke();
            else
                historyItem.OnBackAction?.Invoke();

            AfterLocationChangedHandled?.Invoke(this, EventArgs.Empty);

            whenCompleted.SetResult();
        }
        catch(Exception e) {
            whenCompleted.TrySetException(e);
        }
    }

    private async Task SetStateAsync(State state)
    {
        await _whenJsRefInitialized;
        await _jsRef!.InvokeVoidAsync("setState", state);
    }

    private record PendingHistoryItem(HistoryItem HistoryItem, int HistoryIndex);

    private record HistoryItem(string Uri)
    {
        public Action? OnForwardAction { get; init; }
        public Action? OnBackAction { get; init; }
    }

    public class State
    {
        public int Index { get; init; }
    }

    private enum HistoryMove { Navigate, Forward, Backward }
}
