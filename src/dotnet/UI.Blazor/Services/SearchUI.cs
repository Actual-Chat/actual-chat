using ActualChat.Search;

namespace ActualChat.UI.Blazor.Services;

public class SearchUI : SafeAsyncDisposableBase, IComputeService
{
    public IMutableState<string> Text { get; }
    public IMutableState<bool> IsSearchModeOn { get; }

    protected override Task DisposeAsync(bool disposing)
        => Task.CompletedTask;

    [ComputeMethod]
    public virtual async Task<SearchPhrase> GetSearchPhrase(CancellationToken cancellationToken)
    {
        var text = await Text.Use(cancellationToken).ConfigureAwait(false);
        return text.ToSearchPhrase(true, false);
    }

    public SearchUI(IStateFactory stateFactory)
    {
        Text = stateFactory.NewMutable("", StateCategories.Get(GetType(), nameof(Text)));
        IsSearchModeOn = stateFactory.NewMutable(false, StateCategories.Get(GetType(), nameof(IsSearchModeOn)));
        Text.Updated += (state, _) => {
            var isSearchModeOn = !string.IsNullOrWhiteSpace(state.Value);
            if (IsSearchModeOn.Value != isSearchModeOn)
                IsSearchModeOn.Value = isSearchModeOn;
        };
    }
}
