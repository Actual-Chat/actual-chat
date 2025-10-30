using ActualChat.Kvas;

namespace ActualChat.UI.Blazor.Services;

public class RightPanelStoredState
{
    private const string StatePrefix = nameof(RightPanel) + "UI";
    private const string PanelIsVisibleKey = "RightPanel.IsVisible";

    private readonly StoredState<Box<bool>> _isVisibleStored;

    private UIHub Hub { get; }
    [field: AllowNull, MaybeNull]
    protected LocalSettings WebLocalSettings => field ??= Hub.Services.GetRequiredKeyedService<LocalSettings>(LocalSettings.WebServiceKey);

    public Task WhenRead => _isVisibleStored.WhenRead;

    public bool IsVisible {
        get => _isVisibleStored.Value.Value;
        set {
            var boxedValue = Box.New(value);
            _isVisibleStored.Value = boxedValue;
            _ = SaveIsVisibleState(boxedValue).SilentAwait();
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

    private async Task SaveIsVisibleState(Box<bool> boxedValue)
    {
        var webLocalSettings = WebLocalSettings;
        await webLocalSettings.Set(StatePrefix + "." + PanelIsVisibleKey, boxedValue).SilentAwait();
        await webLocalSettings.Flush().ConfigureAwait(false);
    }
}
