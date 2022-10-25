using ActualChat.Kvas;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Services;

public class RightPanelUI
{
    private readonly IMutableState<bool> _isVisible;

    private HistoryUI HistoryUI { get; }
    private BrowserInfo BrowserInfo { get; }

    public IState<bool> IsVisible
        => _isVisible;

    public RightPanelUI(IServiceProvider services)
    {
        HistoryUI = services.GetRequiredService<HistoryUI>();
        BrowserInfo = services.GetRequiredService<BrowserInfo>();

        var stateFactory = services.StateFactory();
        // TODO: On Desktop we can switch between 2 wide and narrow layout. Decide how to better handle this?
        if (BrowserInfo.ScreenSize.Value.IsNarrow())
            _isVisible = stateFactory.NewMutable<bool>();
        else {
            var localSettings = services.GetRequiredService<LocalSettings>().WithPrefix(nameof(RightPanelUI));
            _isVisible = stateFactory.NewKvasStored<bool>(
                new (localSettings, nameof(IsVisible)) {
                    InitialValue = false,
                });
        }
    }

    public void Switch()
        => ChangeVisibility(!IsVisible.Value);

    public void ChangeVisibility(bool visible)
    {
        if (_isVisible.Value == visible)
            return;

        var screenSize = BrowserInfo.ScreenSize.Value;
        if (screenSize.IsNarrow()) {
            if (visible)
                HistoryUI.NavigateTo(
                    () => ChangeVisibilityInternal(true),
                    () => ChangeVisibilityInternal(false));
            else
                _ = HistoryUI.GoBack();
        }
        else
            ChangeVisibilityInternal(visible);
    }

    private void ChangeVisibilityInternal(bool visible)
        => _isVisible.Value = visible;
}
