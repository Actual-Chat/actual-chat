using ActualChat.Search;

namespace ActualChat.UI.Blazor.Services;

public class SearchUI : SafeAsyncDisposableBase, IComputeService
{
    public IMutableState<string> Text { get; }

    protected override Task DisposeAsync(bool disposing)
        => Task.CompletedTask;

    [ComputeMethod]
    public virtual async Task<SearchPhrase> GetSearchPhrase(CancellationToken cancellationToken)
    {
        var text = await Text.Use(cancellationToken).ConfigureAwait(false);
        return text.ToSearchPhrase(true, false);
    }

    public SearchUI(IStateFactory stateFactory)
        => Text = stateFactory.NewMutable("", StateCategories.Get(GetType(), nameof(Text)));
}
