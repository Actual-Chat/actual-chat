namespace ActualChat.UI.Blazor.Services;

public class SearchUI
{
    public SearchUI(IStateFactory stateFactory)
        => Criteria = stateFactory.NewMutable<string>();

    public IMutableState<string> Criteria { get; }
}
