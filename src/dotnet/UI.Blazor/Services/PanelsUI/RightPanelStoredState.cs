using ActualChat.Kvas;

namespace ActualChat.UI.Blazor.Services;

public class RightPanelStoredState
{
    private const string StatePrefix = nameof(RightPanel) + "UI";
    private const string PanelIsVisibleKey = "RightPanel.IsVisible";
    private const string PanelModeKey = "RightPanel.Mode";

    private readonly StoredState<Box<bool>> _isVisibleStored;
    private readonly StoredState<Box<RightPanelMode>> _modeStored;

    private UIHub Hub { get; }
    protected LocalStorage LocalStorage => Hub.LocalStorage;

    public Task WhenRead => Task.WhenAll(_isVisibleStored.WhenRead, _modeStored.WhenRead);

    public bool IsVisible {
        get => _isVisibleStored.Value.Value;
        set {
            _isVisibleStored.Value = Box.New(value);
            _ = SaveIsVisibleState(value).SilentAwait();
        }
    }

    public RightPanelMode Mode {
        get => _modeStored.Value.Value;
        set => _modeStored.Value = Box.New(value);
    }

    public RightPanelStoredState(UIHub hub)
    {
        Hub = hub;
        var localSettings = hub.LocalSettings.WithPrefix(StatePrefix);
        var stateFactory = hub.StateFactory;
        _isVisibleStored = stateFactory.NewKvasStored<Box<bool>>(
            new (localSettings, PanelIsVisibleKey) {
                InitialValue = Box.New(false),
                Category = StateCategories.Get(GetType(), "IsVisibleStored"),
            });
        _modeStored = stateFactory.NewKvasStored<Box<RightPanelMode>>(
            new (localSettings, PanelModeKey) {
                InitialValue = Box.New(RightPanelMode.Chat),
                Category = StateCategories.Get(GetType(), "ModeStored"),
            });
        // LocalSettings is wiped on a session change, the LocalStorage mirror isn't - so without
        // this the splash skeleton would keep reserving a right panel the app no longer opens.
        _ = _isVisibleStored.WhenRead.ContinueWith(_1 => SaveIsVisibleState(IsVisible), TaskScheduler.Default);
    }

    private async Task SaveIsVisibleState(bool isVisible)
        => await LocalStorage.SetString(StatePrefix + "." + PanelIsVisibleKey, isVisible ? "1" : "0").SilentAwait();
}
