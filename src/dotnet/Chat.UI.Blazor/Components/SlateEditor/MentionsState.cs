namespace ActualChat.Chat.UI.Blazor.Components;

public record Mention(string Id, string Name);

public class MentionCollectionView
{
    private int _index;

    public MentionCollectionView(string search, Mention[] items)
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

public class MentionsState
{
    private MentionCollectionView? _view;

    public event Action<Mention> InsertRequested = _ => { };

    [ComputeMethod]
    public virtual Task<MentionCollectionView?> GetView()
        => Task.FromResult(_view);

    public void ShowMentions(string search)
    {
        if (_view != null && StringComparer.Ordinal.Equals(_view.Search, search))
            return;

        var mentions = GetMentions(search);

        _view = new MentionCollectionView(search, mentions);

        using (Computed.Invalidate())
            _ = GetView();
    }

    public void HideMentions()
    {
        _view = null;
        using (Computed.Invalidate())
            _ = GetView();
    }

    public void MoveDown()
        => _view?.MoveDown();

    public void MoveUp()
        => _view?.MoveUp();

    public void Insert()
    {
        var view = _view;
        if (view == null)
            return;
        var current = view.GetCurrent();
        if (current == null)
            return;
        Insert(current);
    }

    public void Insert(Mention mention)
        => InsertRequested.Invoke(mention);

    private static Mention[] GetMentions(string search)
    {
        Mention[] mentions;
        if (!search.IsNullOrEmpty()) {
            var filter = search.ToLowerInvariant();
            mentions = MentionData.Candidates
                .Where(c => c.Name.StartsWith(filter, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }
        else {
            mentions = MentionData.Candidates;
        }

        return mentions;
    }
}


