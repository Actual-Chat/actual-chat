using ActualChat.Concurrency;
using ActualChat.Hosting;
using ActualChat.UI.Blazor.Module;
using ActualChat.UI.Blazor.Services.Internal;
using Stl.Diagnostics;

namespace ActualChat.UI.Blazor.Services;

public partial class History : IHasServices, IDisposable
{
    private static readonly string JSInitMethod = $"{BlazorUICoreModule.ImportName}.History.init";

    public static readonly TimeSpan MaxNavigationDuration = TimeSpan.FromSeconds(1.5);
    public static readonly TimeSpan AwaitNavigationDuration = TimeSpan.FromSeconds(5);
    public const int MaxItemCount = 200;

    private Session? _session;
    private Dispatcher? _dispatcher;
    private DotNetObjectReference<History>? _blazorRef;
    private readonly TaskCompletionSource _whenReadySource = TaskCompletionSourceExt.New();

    private ILogger Log { get; }
    private ILogger? DebugLog { get; }

    internal object Lock { get; } = new();
    internal HistoryItemIdFormatter ItemIdFormatter { get; }

    // We intentionally expose a number of services here, coz it's convenient to access them via History
    public IServiceProvider Services { get; }
    public Session Session => _session ??= Services.Session();
    public HostInfo HostInfo { get; }
    public UrlMapper UrlMapper { get; }
    public Dispatcher Dispatcher => _dispatcher ??= Services.GetRequiredService<Dispatcher>();
    public NavigationManager Nav { get; }
    public IJSRuntime JS { get; }
    public NavigationQueue NavigationQueue { get; }

    public Task WhenReady => _whenReadySource.Task;

    public HistoryItem? this[long itemId] {
        get { lock (Lock) return GetItemById(itemId); }
    }

    public HistoryItem CurrentItem {
        get { lock (Lock) return _currentItem; }
    }

    public HistoryItem DefaultItem {
        get { lock (Lock) return _defaultItem; }
    }

    public string Uri => LocalUrl.Value;
    public LocalUrl LocalUrl => new(_uri, ParseOrNone.Option);
    public event EventHandler<LocationChangedEventArgs>? LocationChanged;

    public History(IServiceProvider services)
    {
        Services = services;
        Log = services.LogFor(GetType());
        DebugLog = Constants.DebugMode.History ? Log.IfEnabled(LogLevel.Debug) : null;
        ItemIdFormatter = services.GetRequiredService<HistoryItemIdFormatter>();
        HostInfo = services.GetRequiredService<HostInfo>();
        UrlMapper = services.GetRequiredService<UrlMapper>();
        Nav = services.GetRequiredService<NavigationManager>();
        JS = services.JSRuntime();
        NavigationQueue = new NavigationQueue(this); // Services must be initialized before this call

        _isSaveSuppressed = new RegionalValue<bool>(false);
        _saveRegion = new NoRecursionRegion("Save", Lock, Log);
        _locationChangeRegion = new NoRecursionRegion("LocationChange", Lock, Log);
        var isTestServer = HostInfo.AppKind.IsServer() && HostInfo.IsTested;
        if (isTestServer)
            _uri = Links.Home;
        else
            _uri = Nav.GetLocalUrl().Value;
        _defaultItem = new HistoryItem(this, 0, _uri, ImmutableDictionary<Type, HistoryState>.Empty);
        _currentItem = RegisterItem(_defaultItem with { Id = NewItemId() });

        if (!isTestServer)
            Nav.LocationChanged += (_, eventArgs) => LocationChange(eventArgs);
    }

    public void Dispose()
    {
        _blazorRef.DisposeSilently();
        _blazorRef = null;
    }

    public Task Initialize(LocalUrl autoNavigationUrl)
    {
        Log.LogInformation("Initialize @ {AutoNavigationUrl}", autoNavigationUrl);
        _blazorRef = DotNetObjectReference.Create(this);
        var sCurrentItemId = ItemIdFormatter.Format(_currentItem.Id);
        _ = JS.InvokeVoidAsync(JSInitMethod, _blazorRef, autoNavigationUrl.Value, sCurrentItemId);
        return WhenReady;
    }

    public void Register<TState>(TState defaultState, bool ignoreIfAlreadyRegistered = false)
        where TState : HistoryState
    {
        lock (Lock) {
            var stateType = typeof(TState);
            if (_defaultItem.States.ContainsKey(stateType)) {
                if (ignoreIfAlreadyRegistered)
                    return;

                throw StandardError.Internal($"History state of type '{stateType.GetName()}' is already registered.");
            }

            _defaultItem = _defaultItem.With(defaultState);
            var currentItem = _currentItem;
            foreach (var kv in _itemById.List(true).ToList()) {
                var item = kv.Value;
                var existingState = item[stateType];
                if (!Equals(existingState, defaultState)) {
                    var newItem = item.With(defaultState);
                    _itemById[kv.Key] = newItem;
                    if (ReferenceEquals(_currentItem, item))
                        _currentItem = newItem;
                }
            }
            if (ReferenceEquals(currentItem, _currentItem)) {
                Log.LogError("CurrentItem doesn't exist in item list");
                var existingState = _currentItem[stateType];
                if (!Equals(existingState, defaultState))
                    _currentItem = _currentItem.With(defaultState);
            }
        }
    }

    public void FixDefaultState<TState>(
        TState defaultState,
        TState fixedState)
        where TState : HistoryState
    {
        lock (Lock) {
            var stateType = typeof(TState);
            if (!_defaultItem.States.ContainsKey(stateType))
                throw StandardError.Internal($"History state of type '{stateType.GetName()}' is not registered.");

            _defaultItem = _defaultItem.With(fixedState);
            var currentItem = _currentItem;
            foreach (var kv in _itemById.List(true).ToList()) {
                var item = kv.Value;
                var existingState = item[stateType];
                if (ReferenceEquals(existingState, defaultState)) {
                    var newItem = item.With(fixedState);
                    _itemById[kv.Key] = newItem;
                    if (ReferenceEquals(_currentItem, item))
                        _currentItem = newItem;
                }
            }
            if (ReferenceEquals(currentItem, _currentItem)) {
                Log.LogError("CurrentItem doesn't exist in item list");
                var existingState = _currentItem[stateType];
                if (ReferenceEquals(existingState, defaultState))
                    _currentItem = _currentItem.With(fixedState);
            }
        }
    }
}
