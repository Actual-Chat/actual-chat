namespace ActualChat.Users.UI.Blazor.Services;

public class UserSettings
{
    public IMutableState<bool> ShowRightPanelState { get; }

    public UserSettings(IStateFactory stateFactory)
        => ShowRightPanelState = stateFactory.NewMutable(false);
}
