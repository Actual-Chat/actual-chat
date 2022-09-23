using ActualChat.Kvas;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Services;

public class RightPanelUI
{
    public IStoredState<bool> IsVisible { get; }

    public RightPanelUI(IServiceProvider services)
    {
        var stateFactory = services.StateFactory();
        var localSettings = services.GetRequiredService<LocalSettings>().WithPrefix(nameof(RightPanelUI));
        IsVisible = stateFactory.NewKvasStored<bool>(
            new (localSettings, nameof(IsVisible)) {
                InitialValue = false,
            });
    }
}
