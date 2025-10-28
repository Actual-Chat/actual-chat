using ActualChat.Kvas;

namespace ActualChat.UI.Blazor.Services;

public class RightPanelStoredState
{
    private const string StatePrefix = nameof(RightPanel) + "UI";

    private readonly StoredState<Box<bool>> _isVisibleStored;

    public Task WhenRead => _isVisibleStored.WhenRead;

    public bool IsVisible {
        get => _isVisibleStored.Value.Value;
        set => _isVisibleStored.Value = Box.New(value);
    }

    public RightPanelStoredState(UIHub hub)
    {
        var localSettings = hub.LocalSettings.WithPrefix(StatePrefix);
        var stateFactory = hub.StateFactory;
        _isVisibleStored = stateFactory.NewKvasStored<Box<bool>>(
            new (localSettings, "RightPanel.IsVisible") {
                InitialValue = Box.New(false),
                Category = StateCategories.Get(GetType(), "IsVisibleStored"),
            });
    }
}
