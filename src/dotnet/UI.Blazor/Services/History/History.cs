using System.Diagnostics.CodeAnalysis;
using ActualChat.Concurrency;
using ActualChat.Hosting;
using ActualChat.UI.Blazor.Module;
using ActualChat.UI.Blazor.Services.Internal;
using ActualLab.Diagnostics;

namespace ActualChat.UI.Blazor.Services;

public partial class History : ScopedServiceBase<UIHub>, IDisposable
{
    private static readonly string JSInitMethod = $"{BlazorUICoreModule.ImportName}.History.init";

    public const int MaxItemCount = 200;

    private DotNetObjectReference<History>? _blazorRef;
    private readonly TaskCompletionSource _whenReadySource = TaskCompletionSourceExt.New();
    private readonly IMutableState<HistoryItem> _state;

    private new ILogger? DebugLog { get; }

    internal object Lock { get; } = new();
    internal HistoryItemIdFormatter ItemIdFormatter { get; }

    private NavigationManager Nav => Hub.Nav;
    private Dispatcher Dispatcher => Hub.Dispatcher;
    private IJSRuntime JS => Hub.JSRuntime();

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

    public IState<HistoryItem> State => _state;

    public string Uri => LocalUrl.Value;
    public LocalUrl LocalUrl => new(_uri, ParseOrNone.Option);
    public event EventHandler<LocationChangedEventArgs>? LocationChanged;

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(History))]
    public History(UIHub hub) : base(hub)
    {
        DebugLog = Constants.DebugMode.History ? Log.IfEnabled(LogLevel.Debug) : null;
        ItemIdFormatter = Services.GetRequiredService<HistoryItemIdFormatter>();
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
        _state = StateFactory.NewMutable(_currentItem, StateCategories.Get(GetType(), nameof(State)));

        if (!isTestServer)
            Nav.LocationChanged += (_, eventArgs) => LocationChange(eventArgs);
    }

    public void Dispose()
    {
        _whenReadySource.TrySetCanceled();
        _blazorRef.DisposeSilently();
        _blazorRef = null;
    }

    public async Task Initialize(LocalUrl autoNavigationUrl)
    {
        Log.LogInformation("Initialize @ {AutoNavigationUrl}", autoNavigationUrl);
        _blazorRef = DotNetObjectReference.Create(this);
        var sCurrentItemId = ItemIdFormatter.Format(_currentItem.Id);
        var initTask = JS.InvokeVoidAsync(JSInitMethod, _blazorRef, autoNavigationUrl.Value, sCurrentItemId);
        await initTask.ConfigureAwait(false);
        try {
            await WhenReady.WaitAsync(TimeSpan.FromSeconds(0.5)).ConfigureAwait(false);
        }
        catch (TimeoutException) {
            Log.LogWarning("Initialize: timed out");
            _whenReadySource.TrySetResult();
        }
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
}
