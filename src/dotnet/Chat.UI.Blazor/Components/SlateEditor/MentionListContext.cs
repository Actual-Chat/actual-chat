namespace ActualChat.Chat.UI.Blazor.Components;

public record Mention(string Id, string Name);

public class MentionListViewModel
{
    private int _index;

    public MentionListViewModel(string search, Mention[] items)
    {
        Search = search;
        Items = items;
        _index = items.Length > 0 ? 0 : -1;
    }

    public string Search { get; }

    public Mention[] Items { get; }

    public int CurrentIndex => _index;

    public Mention? GetCurrent()
        => _index >= 0 ? Items[_index] : null;

    public event Action CurrentIndexChange = () => { };

    private void InnerSetCurrentIndex(int index)
    {
        if (index < 0 || index >= Items.Length)
            return;
        _index = index;
        CurrentIndexChange();
    }

    public void SetCurrentIndex(int index)
        => InnerSetCurrentIndex(index);

    public void MoveDown()
    {
        var newIndex = CurrentIndex + 1;
        if (newIndex >= Items.Length)
            newIndex = 0;
        InnerSetCurrentIndex(newIndex);
    }

    public void MoveUp()
    {
        var newIndex = CurrentIndex - 1;
        if (newIndex < 0)
            newIndex = Items.Length - 1;
        InnerSetCurrentIndex(newIndex);
    }
}

public interface IMentionsRetriever
{
    Task<IEnumerable<Mention>> GetMentions(string search, int limit, CancellationToken cancellationToken);
}

public class MentionListContext
{
    private MentionListViewModel? _viewModel;
    private IMentionsRetriever? _mentionsRetriever;

    public event Action<Mention> InsertRequested = _ => { };

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

    public void Insert(Mention mention)
        => InsertRequested.Invoke(mention);

    private async Task<Mention[]> GetMentions(string search, CancellationToken cancellationToken)
    {
        const int limit = 10;
        var mentions = _mentionsRetriever != null
            ? await _mentionsRetriever.GetMentions(search, limit, cancellationToken).ConfigureAwait(false)
            : GetDemoMentions(search);
        return mentions.Take(limit).ToArray();
    }

    private static IEnumerable<Mention> GetDemoMentions(string search)
    {
        if (search.IsNullOrEmpty())
            return MentionData.Candidates;
        var filter = search.ToLowerInvariant();
        return MentionData.Candidates
                .Where(c => c.Name.StartsWith(filter, StringComparison.OrdinalIgnoreCase));
    }
}


