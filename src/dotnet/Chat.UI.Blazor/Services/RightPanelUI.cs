namespace ActualChat.Chat.UI.Blazor.Services;

public class RightPanelUI
{
    public IMutableState<bool> IsVisible { get; }

    public RightPanelUI(IStateFactory stateFactory)
        => IsVisible = stateFactory.NewMutable(true);
}
