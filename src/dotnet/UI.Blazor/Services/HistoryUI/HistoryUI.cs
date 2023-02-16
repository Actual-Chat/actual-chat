using ActualChat.Concurrency;
using ActualChat.Hosting;

namespace ActualChat.UI.Blazor.Services;

public partial class HistoryUI : IHasServices
{
    public const int MaxPosition = 1000;
    public const int MaxItemCount = 200;

    private object Lock { get; } = new();
    private ILogger Log { get; }
    private ILogger? DebugLog => Constants.DebugMode.HistoryUI ? Log : null;

    // We intentionally expose a number of services here, coz it's convenient to access them via HistoryUI
    public IServiceProvider Services { get; }
    public HistoryHub Hub { get; }

    public HistoryItem? this[long itemId] {
        get { lock (Lock) return GetItemByIdUnsafe(itemId); }
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

    public HistoryUI(IServiceProvider services)
    {
        Services = services;
        Log = services.LogFor(GetType());
        Hub = services.GetRequiredService<HistoryHub>();
        _isSaveSuppressed = new LocalValue<bool>(false);
        _saveRegion = new LockedRegionWithExitAction("Save", Lock);
        _locationChangeRegion = new LockedRegionWithExitAction("LocationChange", Lock);

        _uri = Hub.Nav.GetLocalUrl().Value;
        _defaultItem = new HistoryItem(this, 0, _uri, ImmutableDictionary<Type, HistoryState>.Empty);
        _currentItem = RegisterItem(_defaultItem with { Id = NewItemId() });

        if (!Hub.HostInfo.AppKind.IsTestServer())
            Hub.Nav.LocationChanged += (_, eventArgs) => LocationChange(eventArgs);
    }

    public void Initialize()
        => LocationChange(new LocationChangedEventArgs(Hub.Nav.Uri, true));

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
