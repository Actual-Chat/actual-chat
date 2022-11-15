namespace ActualChat.Chat.UI.Blazor.Services;

public class PlayableTextPaletteProvider
{
    private const int MaxPalettesNumber = 5;
    private readonly LinkedList<(ChatId ChatId, PlayableTextPalette Palette)> _cache = new ();

    public PlayableTextPalette GetPalette(ChatId chatId)
    {
        PlayableTextPalette palette;
        lock (_cache) {
            if (_cache.Count == 0) {
                palette = NewPalette();
                _cache.AddFirst((chatId, palette));
            }
            else {
                var first = _cache.First!;
                if (first.Value.ChatId == chatId)
                    palette = first.Value.Palette;
                else {
                    var node = first.Next;
                    while (node != null) {
                        if (node.Value.ChatId == chatId)
                            break;
                        node = node.Next;
                    }
                    if (node != null) {
                        palette = node.Value.Palette;
                        _cache.Remove(node);
                        _cache.AddFirst(node.Value);
                    }
                    else {
                        while (_cache.Count >= MaxPalettesNumber)
                            _cache.RemoveLast();
                        palette = NewPalette();
                        _cache.AddFirst((chatId, palette));
                    }
                }
            }
        }
        return palette;
    }

    private static PlayableTextPalette NewPalette() => new ();
}
