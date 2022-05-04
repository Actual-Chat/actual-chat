namespace ActualChat.Users.UI.Blazor.Services;

public class UserSettings
{
    public IMutableState<bool> IsRightPanelShown { get; }

    public UserSettings(IStateFactory stateFactory)
        => IsRightPanelShown = stateFactory.NewMutable(false);
}
