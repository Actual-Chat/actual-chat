namespace ActualChat.UI.Blazor.Services;

public class SearchUI
{
    public IMutableState<string> Text { get; }

    [ComputeMethod]
    public virtual async Task<SearchPhrase> GetSearchPhrase(CancellationToken cancellationToken)
    {
        var text = await Text.Use(cancellationToken).ConfigureAwait(false);
        return text.ToSearchPhrase();
    }

    public SearchUI(IStateFactory stateFactory)
        => Text = stateFactory.NewMutable<string>();
}
