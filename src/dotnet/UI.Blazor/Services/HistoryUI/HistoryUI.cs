using ActualChat.Hosting;
using ActualChat.UI.Blazor.Services.Internal;

namespace ActualChat.UI.Blazor.Services;

public partial class HistoryUI : IHasServices
{
    public const int MaxPosition = 1000;
    public const int MaxItemCount = 200;

    private volatile string _uri;

    private object Lock { get; } = new();
    private ILogger Log { get; }
    private ILogger? DebugLog => Constants.DebugMode.HistoryUI ? Log : null;

    // We intentionally expose a number of services here, coz it's convenient to access them via HistoryUI
    public IServiceProvider Services { get; }
    public HistoryHub Hub { get; }

    public int Position {
        get { lock (Lock) return _position; }
    }

    public HistoryItem CurrentItem {
        get { lock (Lock) return CurrentItemUnsafe; }
    }

    public HistoryItem DefaultItem {
        get { lock (Lock) return _defaultItem; }
    }

    public string Uri => LocalUrl.Value;
    public LocalUrl LocalUrl => new LocalUrl(_uri, ParseOrNone.Option);

    public HistoryUI(IServiceProvider services)
    {
        Services = services;
        Log = services.LogFor(GetType());
        Hub = services.GetRequiredService<HistoryHub>();

        var uri = _uri = Hub.Nav.GetLocalUrl().Value;
        _defaultItem = new HistoryItem(0, 0, uri, ImmutableDictionary<Type, HistoryState>.Empty);
        var firstItem = new HistoryItem(NextItemId(), 0, uri, ImmutableDictionary<Type, HistoryState>.Empty);
        AddHistoryItem(firstItem);

        if (!Hub.HostInfo.AppKind.IsTestServer())
            Hub.Nav.LocationChanged += OnLocationChanged;
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
            foreach (var kv in _itemById.List(true).ToList()) {
                var item = kv.Value;
                var existingState = item[stateType];
                if (!Equals(existingState, defaultState)) {
                    item = item.With(defaultState);
                    _itemById[kv.Key] = item;
                }
            }
        }
    }
}
