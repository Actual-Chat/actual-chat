namespace ActualChat.UI.Blazor.App.Components;

internal class VisualMediaGalleryArrangeResultBuilder<TItem>
{
    private readonly List<VisualMediaGalleryLine<TItem>> _lines = new();
    private readonly List<VisualMediaGalleryTile<TItem>> _line = new();

    public void AddTile(VisualMediaGalleryTile<TItem> tile)
        => _line.Add(tile);

    public IReadOnlyList<VisualMediaGalleryTile<TItem>> Line => _line;

    public void CompleteLine()
    {
        if (_line.Count == 0)
            return;

        _lines.Add(new VisualMediaGalleryLine<TItem>(_line.ToArray()));
        _line.Clear();
    }

    public VisualMediaGalleryLine<TItem>[] GetLines()
        => _lines.ToArray();
}
