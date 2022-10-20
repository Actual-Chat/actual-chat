using System.Web;
using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public interface IHistoryUIBackend
{
    Task OnPopState(string state);
}

// Keeps track of the initial location.
// Service should be instantiated on app start.
public class HistoryUI : IHistoryUIBackend, IDisposable
{
    private readonly Stack<HistoryItem> _history = new ();
    private readonly List<string> _log = new ();
    private readonly Task _whenInitialized;
    private PendingHistoryItem? _pendingHistoryItem = null;
    private string _prevMarker;
    private string _marker;
    private DotNetObjectReference<IHistoryUIBackend>? _blazorRef;

    private BrowserInfo BrowserInfo { get; }
    private IJSRuntime JS { get; }
    private ILogger<HistoryUI> Log { get; }
    private NavigationManager Nav { get; }

    public string InitialUri { get; }
    public bool IsInitialLocation { get; private set; }

    public string Marker => _marker;

    public bool IsChatLoadRedirectRequested { get; private set; }

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

        InitialUri = Nav.Uri;
        IsInitialLocation = true;
        _history.Push(new HistoryItem(InitialUri));
        _marker = MakeNewMarker;
        _prevMarker = "";
        LogLocation(InitialUri);
        Log.LogInformation("Initial location: '{Location}'", Nav.Uri);

        // HistoryUI is initialized upon BlazorCircuitContext is created.
        // At this moment there is no yet subscriber to Nav.LocationChanged.
        // So HistoryUI will be notified first on location changed, even before router.
        Nav.LocationChanged += OnLocationChanged;

        _blazorRef = DotNetObjectReference.Create<IHistoryUIBackend>(this);
        _whenInitialized = JS.InvokeVoidAsync(
            $"{BlazorUICoreModule.ImportName}.HistoryUI.create",
            _blazorRef)
            .AsTask();
    }

    public void Dispose()
        => _blazorRef?.Dispose();

    private void LogLocation(string location)
        => _log.Add(_marker + " : " + location);

    private string MakeNewMarker => Guid.NewGuid().ToString();

    [JSInvokable]
    public Task OnPopState(string state)
    {
        return Task.CompletedTask;
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        IsInitialLocation = false;

        _prevMarker = _marker;
        _marker = MakeNewMarker;

        var location = e.Location;
        Log.LogInformation("Location changed: '{Location}'", location);
        LogLocation(location);

        //Nav.LocationChanged -= OnLocationChanged;

        if (!BrowserInfo.ScreenSize.Value.IsNarrow())
            return;

        // TODO: in .NET 7 NavigationManager provides access to state property of the history API.
        // rework history position tracking with it.

        var move = 0;
        if (_pendingHistoryItem != null) {
            if (TryGetMarker(location, out var locationMarker)) {
                if (StringComparer.Ordinal.Equals(_pendingHistoryItem.Marker, locationMarker)) {
                    var historyItem = _pendingHistoryItem.HistoryItem;
                    _history.Push(historyItem);
                    var onForwardAction = historyItem.OnForwardAction;
                    _pendingHistoryItem = null;
                    if (onForwardAction != null)
                        onForwardAction.Invoke();
                    move = 1;
                }
            }
        }

        if (move == 0) {
            if (_history.Count > 1) {
                var items = _history.ToArray();
                var lastItem = items[0];
                var preItem = items[1];
                if (StringComparer.Ordinal.Equals(preItem.Uri, location)) {
                    // on back action detected
                    // do we need to detect or better to listen on popstate in js?
                    _history.Pop();
                    move = -1;
                    if (lastItem.OnBackAction != null)
                        lastItem.OnBackAction();
                }
            }
        }

        if (move == 0) {
            move = 1;
            _history.Push(new HistoryItem(location));
        }

        // else {
        //     var handled = false;
        //     if (_history.Count > 1) {
        //         var items = _history.ToArray();
        //         var lastItem = items[0];
        //         var preItem = items[1];
        //         if (StringComparer.Ordinal.Equals(preItem.Uri, location)) {
        //             // on back action detected
        //             // do we need to detect or better to listen on popstate in js?
        //             _history.Pop();
        //             handled = true;
        //             if (lastItem.OnBackAction != null)
        //                 lastItem.OnBackAction();
        //         }
        //     }
        //     if (!handled) {
        //         _history.Push(new HistoryItem(location));
        //     }
        // }}
    }

    private bool TryGetMarker(string location, out string? marker)
    {
        var uri = new Uri(location);
        var queryParameters = HttpUtility.ParseQueryString(uri.Query);
        marker = queryParameters.Get("marker");
        return !string.IsNullOrEmpty(marker);
    }

    public async Task GoBack()
    {
        try {
            await JS.InvokeVoidAsync("eval", "history.back()");
        }
        catch (Exception e) {

        }
    }

    // public void NavigateTo(string uri, bool replace, Action? onForwardAction, Action? onBackAction)
    // {
    //     var historyItem = new HistoryItem(uri) {
    //         OnForwardAction = onForwardAction,
    //         OnBackAction = onBackAction
    //     };
    //     _pendingHistoryItem = new PendingHistoryItem(historyItem, _marker);
    //     Nav.NavigateTo(uri, false, replace);
    // }

    public void NavigateTo(Action? onForwardAction, Action? onBackAction)
    {
        if (_pendingHistoryItem != null)
            throw StandardError.Constraint("There is still pending history item");
        var uri = GetUriWithMarker(Nav.Uri, _marker);
        var historyItem = new HistoryItem(uri) {
            OnForwardAction = onForwardAction,
            OnBackAction = onBackAction
        };
        _pendingHistoryItem = new PendingHistoryItem(historyItem, _marker);
        Nav.NavigateTo(uri);
    }

    private string GetUriWithMarker(string uri, string marker)
    {
        var index = uri.IndexOf("?");
        if (index >= 0)
            uri = uri.Substring(0, index);
        uri += "?marker=" + marker.UrlEncode();
        return uri;
    }

    public void DemandChatLoadRedirect(bool demand)
        => IsChatLoadRedirectRequested = demand;

    private record PendingHistoryItem(HistoryItem HistoryItem, string Marker);

    private record HistoryItem(string Uri)
    {
        public Action? OnForwardAction { get; init; }
        public Action? OnBackAction { get; init; }
    };
}
