using ActualChat.Kvas;

namespace ActualChat.UI.Blazor.Services;

public class RightPanelStoredState
{
    private const string StatePrefix = nameof(RightPanel) + "UI";
    private const string PanelIsVisibleKey = "RightPanel.IsVisible";

    private readonly StoredState<Box<bool>> _isVisibleStored;

    private UIHub Hub { get; }
    protected LocalStorage LocalStorage => Hub.LocalStorage;

    public Task WhenRead => _isVisibleStored.WhenRead;

    public bool IsVisible {
        get => _isVisibleStored.Value.Value;
        set {
            _isVisibleStored.Value = Box.New(value);
            _ = SaveIsVisibleState(value).SilentAwait();
        }
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
    }

    private async Task SaveIsVisibleState(bool isVisible)
        => await LocalStorage.SetString(StatePrefix + "." + PanelIsVisibleKey, isVisible ? "1" : "0").SilentAwait();
}
