namespace ActualChat.Chat.UI.Blazor.Services;

public class PlayableTextPaletteProvider
{
    private const int MaxPalettesNumber = 5;
    private readonly LinkedList<(Symbol, PlayableTextPalette)> _cache = new ();

    public PlayableTextPalette GetPalette(Symbol chatId)
    {
        PlayableTextPalette palette;
        lock (_cache) {
            if (_cache.Count == 0) {
                palette = CreatePalette();
                _cache.AddFirst((chatId, palette));
            }
            else {
                var first = _cache.First!;
                if (first.Value.Item1 == chatId)
                    palette = first.Value.Item2;
                else {
                    var node = first.Next;
                    while (node != null) {
                        if (node.Value.Item1 == chatId)
                            break;
                        node = node.Next;
                    }
                    if (node != null) {
                        palette = node.Value.Item2;
                        _cache.Remove(node);
                        _cache.AddFirst(node.Value);
                    }
                    else {
                        while (_cache.Count >= MaxPalettesNumber)
                            _cache.RemoveLast();
                        palette = CreatePalette();
                        _cache.AddFirst((chatId, palette));
                    }
                }
            }
        }
        return palette;
    }

    private static PlayableTextPalette CreatePalette()
        => new PlayableTextPalette();
}
