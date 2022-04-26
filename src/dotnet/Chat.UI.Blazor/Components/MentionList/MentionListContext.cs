namespace ActualChat.Chat.UI.Blazor.Components;

public class MentionListContext
{
    private MentionListViewModel? _viewModel;
    private IMentionsRetriever? _mentionsRetriever;

    public event Action<MentionListItem> InsertRequested = _ => { };

    [ComputeMethod]
    public virtual Task<MentionListViewModel?> GetViewModel()
        => Task.FromResult(_viewModel);

    public void SetMentionsRetriever(IMentionsRetriever mentionsRetriever)
        => _mentionsRetriever = mentionsRetriever;

    public async Task ShowMentions(string search)
    {
        if (_viewModel != null && StringComparer.Ordinal.Equals(_viewModel.Search, search))
            return;

        var mentions = await GetMentions(search, default).ConfigureAwait(false);

        _viewModel = new MentionListViewModel(search, mentions);

        using (Computed.Invalidate())
            _ = GetViewModel();
    }

    public void HideMentions()
    {
        _viewModel = null;
        using (Computed.Invalidate())
            _ = GetViewModel();
    }

    public void MoveDown()
        => _viewModel?.MoveDown();

    public void MoveUp()
        => _viewModel?.MoveUp();

    public void Insert()
    {
        var view = _viewModel;
        if (view == null)
            return;
        var current = view.GetCurrent();
        if (current == null)
            return;
        Insert(current);
    }

    public void Insert(MentionListItem mentionListItem)
        => InsertRequested.Invoke(mentionListItem);

    private async Task<MentionListItem[]> GetMentions(string search, CancellationToken cancellationToken)
    {
        const int limit = 10;
        var mentions = _mentionsRetriever != null
            ? await _mentionsRetriever.GetMentions(search, limit, cancellationToken).ConfigureAwait(false)
            : GetDemoMentions(search);
        return mentions.Take(limit).ToArray();
    }

    private static IEnumerable<MentionListItem> GetDemoMentions(string search)
    {
        if (search.IsNullOrEmpty())
            return MentionTestData.Candidates;
        var filter = search.ToLowerInvariant();
        return MentionTestData.Candidates
                .Where(c => c.Name.Contains(filter, StringComparison.OrdinalIgnoreCase));
    }
}
