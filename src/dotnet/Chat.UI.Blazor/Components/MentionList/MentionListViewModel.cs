namespace ActualChat.Chat.UI.Blazor.Components;

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
