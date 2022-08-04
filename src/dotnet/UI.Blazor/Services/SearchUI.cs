namespace ActualChat.UI.Blazor.Services;

public class SearchUI
{
    public IMutableState<string> Criteria { get; }

    public SearchUI(IStateFactory stateFactory)
        => Criteria = stateFactory.NewMutable<string>();
}
