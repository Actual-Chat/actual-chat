using ActualChat.Hosting;

namespace ActualChat.UI.Blazor.Services;

// Keeps track of the initial location.
// Service should be instantiated on app start.
public class HistoryUI
{
    private const string MarkerPrefix = "marker:";
    private int _position;
    private readonly List<HistoryItem> _history = new ();
    private PendingHistoryItem? _pendingHistoryItem;
    private readonly string _initialLocation;
    private bool _rewriteInitialLocation;
    private readonly bool _isTestServer;
    private TaskSource<Unit> _whenInitializedSource;

    private IJSRuntime JS { get; }
    private ILogger Log { get; }
    private NavigationManager Nav { get; }

    public bool IsInitialLocation { get; private set; }
    public Task WhenInitialized { get; }

    public event EventHandler<EventArgs>? AfterLocationChangedHandled;

    public HistoryUI(IServiceProvider services)
    {
        Log = services.LogFor(GetType());
        JS = services.GetRequiredService<IJSRuntime>();
        Nav = services.GetRequiredService<NavigationManager>();
        var hostInfo = services.GetRequiredService<HostInfo>();
        _isTestServer = hostInfo.AppKind == AppKind.TestServer;

        IsInitialLocation = true;
        if (_isTestServer) {
            _initialLocation = "";
            _position = 0;
        }
        else {
            var uri = Nav.Uri;
            _initialLocation = Nav.GetLocalUrl();
            _position = 0;
            _history.Add(new HistoryItem(uri, new State()));
            Log.LogDebug("Initial location: '{Location}'", uri);

            // HistoryUI is initialized upon BlazorCircuitContext is created.
            // At this moment there is no yet subscribers to Nav.LocationChanged.
            // So HistoryUI will be notified the first on location changed, even before the Router.
            Nav.LocationChanged += OnLocationChanged;
        }

        WhenInitialized = Initialize();
    }

    private async Task Initialize()
    {
        if (_isTestServer)
            return;

        var url = Nav.GetLocalUrl();
        // We need at least one history item to enable proper left/right drawer support on chats page
        var mustRedirectToChats = url.IsChatOrChatRoot();
        if (mustRedirectToChats) {
            _rewriteInitialLocation = true;
            _whenInitializedSource = TaskSource.New<Unit>(true);
            Log.LogDebug("Redirecting to chats from '{InitialLocation}' to create history item", url);
            Nav.NavigateTo(Links.Chat(default), false, true);
            await _whenInitializedSource.Task.ConfigureAwait(true);
            _whenInitializedSource = TaskSource<Unit>.Empty;
        }
    }

    public Task GoBack()
    {
        if (_isTestServer)
            return Task.CompletedTask;

        Log.LogDebug("About to go back");
        return JS.InvokeVoidAsync("eval", "history.back()").AsTask();
    }

    public void NavigateTo(Action? onForwardAction, Action? onBackAction)
    {
        if (_isTestServer)
            return;

        if (_pendingHistoryItem != null)
            throw StandardError.Internal("There is still pending history item");
        var historyItem = new HistoryItemPrototype(Nav.Uri) {
            OnForwardAction = onForwardAction,
            OnBackAction = onBackAction,
        };
        _pendingHistoryItem = new PendingHistoryItem(historyItem, Ulid.NewUlid().ToString());
        Nav.NavigateTo(Nav.Uri, new NavigationOptions {
            ForceLoad = false,
            ReplaceHistoryEntry = false,
            HistoryEntryState = MarkerPrefix + _pendingHistoryItem.Marker,
        });
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        Log.LogDebug("Location changed: '{Location}'. State: '{State}'. History.Count: {Count}, Position: {Position}.",
            e.Location, e.HistoryEntryState, _history.Count, _position);

        if (_rewriteInitialLocation) {
            _rewriteInitialLocation = false;
            Nav.NavigateTo(_initialLocation);
            return;
        }

        // TODO(DF): in windows chrome I have an issue.
        // After initial location rewritten clicking on back button in chrome
        // redirects to 'chrome://new-tab-page/' and not to '/chats/'.
        // While calling 'history.back()' works as expected.

        if (!_whenInitializedSource.IsEmpty)
            _whenInitializedSource.TrySetResult(default);

        IsInitialLocation = false;

        var state = GetState(e.HistoryEntryState);
        OnLocationChanged(e.Location, state);

        Log.LogDebug("Location changed completed. History.Count: {Count}. Position: {Position}.",
            _history.Count, _position);
    }

    private void OnLocationChanged(string location, State state)
    {
        HistoryMove move;
        var readPosition = state.StepIndex;
        if (readPosition < _position)
            move = HistoryMove.Backward;
        else if (readPosition > _position) {
            var isExistingHistoryItem = _history.Any(c => OrdinalEquals(c.State.Id, state.Id));
            move = isExistingHistoryItem ? HistoryMove.Forward : HistoryMove.Navigate;
        }
        else {
            // History index hasn't changed -> navigation with replace happened
            move = HistoryMove.NavigateWithReplace;
        }

        Log.LogDebug("Move '{Move}' evaluated", move);

        HistoryItem? historyItem = null;
        if (move == HistoryMove.Navigate || move == HistoryMove.NavigateWithReplace) {
            if (_pendingHistoryItem != null) {
                var marker = !state.UserState.IsNullOrEmpty() && state.UserState.OrdinalStartsWith(MarkerPrefix)
                    ? state.UserState[MarkerPrefix.Length..]
                    : "";
                if (!marker.IsNullOrEmpty() && OrdinalEquals(_pendingHistoryItem.Marker, marker)) {
                    if (!OrdinalEquals(_pendingHistoryItem.Prototype.Uri, location))
                        throw StandardError.Internal("PendingHistoryItem is not consistent. Location is wrong.");

                    var prototype = _pendingHistoryItem.Prototype;
                    historyItem = new HistoryItem(prototype.Uri, state) {
                        OnForwardAction = prototype.OnForwardAction,
                        OnBackAction = prototype.OnBackAction,
                    };
                    _pendingHistoryItem = null;
                }
            }
            historyItem ??= new HistoryItem(location, state);
            if (_history.Count < readPosition)
                throw StandardError.Internal("History is not consistent.");
            if (_history.Count > readPosition)
                _history.RemoveRange(readPosition, _history.Count - readPosition);
            _history.Add(historyItem);
        }
        else if (move == HistoryMove.Forward) {
            if (_history.Count < readPosition + 1)
                throw StandardError.Internal("History does not contain forward item.");
            historyItem = _history[readPosition];
        }
        else {
            if (_history.Count < readPosition + 2)
                throw StandardError.Internal("History does not contain backward item.");
            // When going backward, we need to get data from last history item.
            // Of even from all previous items as well till readPosition,
            // but we need to check if associated components are closed already or not.
            historyItem = _history[_position];
        }

        _position = readPosition;
        if (move != HistoryMove.Backward)
            historyItem.OnForwardAction?.Invoke();
        else
            historyItem.OnBackAction?.Invoke();

        AfterLocationChangedHandled?.Invoke(this, EventArgs.Empty);
    }

    private State GetState(string? state)
    {
        if (state.IsNullOrEmpty())
            return new State();
 #pragma warning disable IL2026
        return JsonSerializer.Deserialize<State>(state)!;
 #pragma warning restore IL2026
    }

    private record PendingHistoryItem(
        HistoryItemPrototype Prototype,
        string Marker);

    private record HistoryItem(string Uri, State State)
    {
        public Action? OnForwardAction { get; init; }
        public Action? OnBackAction { get; init; }
    }

    private record HistoryItemPrototype(string Uri)
    {
        public Action? OnForwardAction { get; init; }
        public Action? OnBackAction { get; init; }
    }

    private enum HistoryMove { Navigate, NavigateWithReplace, Forward, Backward }

    public record State
    {
        [JsonPropertyName("_index")]
        public int Index { get; init; }
        [JsonPropertyName("_stepIndex")]
        public int StepIndex { get; init; }
        [JsonPropertyName("_id")]
        public string Id { get; init; } = "";
        [JsonPropertyName("userState")]
        public string? UserState { get; init; }
    }
}
