namespace ActualChat.Chat.UI.Blazor.Services;

public class RightPanelSettings
{
    public IMutableState<bool> IsPanelShown { get; }

    public IMutableState<ChatRightPanel.Page> SelectedPage { get; }

    public RightPanelSettings(IStateFactory stateFactory)
    {
        IsPanelShown = stateFactory.NewMutable(false);
        SelectedPage = stateFactory.NewMutable(ChatRightPanel.Page.Users);
    }
}
