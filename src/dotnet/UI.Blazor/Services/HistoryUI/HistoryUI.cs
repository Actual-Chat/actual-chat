using ActualChat.Hosting;
using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

// Keeps track of the initial location.
// Service should be instantiated on app start.
public class HistoryUI
{
    private readonly List<HistoryItem> _history = new ();
    private PendingHistoryItem? _pendingHistoryItem;
    private IJSObjectReference? _jsRef;
    private int _historyIndex;
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
            _historyIndex = 0;
        }
        else {
            var uri = Nav.Uri;
            _initialLocation = Nav.GetLocalUrl();
            _historyIndex = 0;
            _history.Add(new HistoryItem(uri, ""));
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

        _jsRef = await JS.InvokeAsync<IJSObjectReference>($"{BlazorUICoreModule.ImportName}.HistoryUI.create");

        var url = Nav.GetLocalUrl();
        if (!url.IsHome() && !url.IsChatRoot()) {
            _rewriteInitialLocation = true;
            _whenInitializedSource = TaskSource.New<Unit>(true);
            Log.LogDebug("Rewrite initial location from '{InitialLocation}'", url);
            Nav.NavigateTo(Links.Chat(default), false, true);
            await _whenInitializedSource.Task.ConfigureAwait(true);
            _whenInitializedSource = TaskSource<Unit>.Empty;
        }
    }

    public Task GoBack()
    {
        if (_isTestServer)
            return Task.CompletedTask;

        return JS.InvokeVoidAsync("eval", "history.back()").AsTask();
    }

    public void NavigateTo(Action? onForwardAction, Action? onBackAction)
    {
        if (_isTestServer)
            return;

        if (_pendingHistoryItem != null)
            throw StandardError.Constraint("There is still pending history item");
        var historyItem = new HistoryItemPrototype(Nav.Uri) {
            OnForwardAction = onForwardAction,
            OnBackAction = onBackAction
        };
        _pendingHistoryItem = new PendingHistoryItem(historyItem, _historyIndex);
        Nav.NavigateTo(Nav.Uri);
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        Log.LogDebug("Location changed: '{Location}', State: '{State}'", e.Location, e.HistoryEntryState);

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
    }

    private void OnLocationChanged(string location, State state)
    {
        HistoryMove move;
        var readHistoryIndex = state.Index;
        if (readHistoryIndex < _historyIndex)
            move = HistoryMove.Backward;
        else if (readHistoryIndex > _historyIndex) {
            var isExistingHistoryItem = _history.Any(c => OrdinalEquals(c.Id, state.Id));
            move = isExistingHistoryItem ? HistoryMove.Forward : HistoryMove.Navigate;
        }
        else {
            // history index has not changed
            // navigation with replace happened
            move = HistoryMove.Navigate;
        }

        HistoryItem historyItem;
        if (move == HistoryMove.Navigate) {
            if (_pendingHistoryItem != null) {
                if (_pendingHistoryItem.HistoryIndex != _historyIndex)
                    throw StandardError.Constraint("PendingHistoryItem is not consistent.");
                if (!OrdinalEquals(_pendingHistoryItem.HistoryItem.Uri, location))
                    throw StandardError.Constraint("PendingHistoryItem is not consistent. Location is wrong.");

                var prototype = _pendingHistoryItem.HistoryItem;
                historyItem = new HistoryItem(prototype.Uri, state.Id) {
                    OnForwardAction = prototype.OnForwardAction,
                    OnBackAction = prototype.OnBackAction,
                };
                _pendingHistoryItem = null;
            }
            else
                historyItem = new HistoryItem(location, state.Id);
            if (_history.Count < readHistoryIndex)
                throw StandardError.Constraint("History is not consistent.");
            if (_history.Count > readHistoryIndex)
                _history.RemoveRange(readHistoryIndex, _history.Count - readHistoryIndex);
            _history.Add(historyItem);
        }
        else if (move == HistoryMove.Forward) {
            if (_history.Count < readHistoryIndex + 1)
                throw StandardError.Constraint("History does not contain forward item.");
            historyItem = _history[readHistoryIndex];
        }
        else {
            if (_history.Count < readHistoryIndex + 2)
                throw StandardError.Constraint("History does not contain backward item.");
            historyItem = _history[readHistoryIndex + 1];
        }

        _historyIndex = readHistoryIndex;

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

    private record PendingHistoryItem(HistoryItemPrototype HistoryItem, int HistoryIndex);

    private record HistoryItem(string Uri, string Id)
    {
        public Action? OnForwardAction { get; init; }
        public Action? OnBackAction { get; init; }
    }

    private record HistoryItemPrototype(string Uri)
    {
        public Action? OnForwardAction { get; init; }
        public Action? OnBackAction { get; init; }
    }

    private enum HistoryMove { Navigate, Forward, Backward }

    public record State
    {
        [JsonPropertyName("_index")]
        public int Index { get; init; }
        [JsonPropertyName("_id")]
        public string Id { get; init; } = "";
        [JsonPropertyName("userState")]
        public string? UserState { get; init; }
    }
}
