using ActualChat.Hosting;
using ActualChat.UI.Blazor.Services.Internal;

namespace ActualChat.UI.Blazor.Services;

public partial class HistoryUI : IHasServices
{
    private const int MaxFixCount = 16;

    public const int MaxPosition = 1000;
    public const int MaxItemCount = 100;

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

    public string Uri => CurrentItem.Uri;
    public LocalUrl LocalUrl => CurrentItem.LocalUrl;

    public HistoryUI(IServiceProvider services)
    {
        Services = services;
        Log = services.LogFor(GetType());
        Hub = services.GetRequiredService<HistoryHub>();

        _defaultItem = new HistoryItem(0, 0, ImmutableDictionary<Type, HistoryState>.Empty);
        var uriState = new UriState(Hub.Nav);
        Register(uriState);
        var firstItem = new HistoryItem(NextItemId(), 0, ImmutableDictionary<Type, HistoryState>.Empty).With(uriState);
        AddOrReplaceHistoryItem(firstItem, false);

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
