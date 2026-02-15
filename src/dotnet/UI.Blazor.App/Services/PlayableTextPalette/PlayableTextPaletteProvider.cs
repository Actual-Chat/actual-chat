namespace ActualChat.UI.Blazor.App.Services;

public sealed class PlayableTextPaletteProvider
{
    private const int MaxPalettesNumber = 5;
    private readonly ThreadSafeLruCache<ChatId, PlayableTextPalette> _cache = new (MaxPalettesNumber);

    public PlayableTextPalette GetPalette(ChatId chatId)
        => _cache.GetOrCreate(chatId, _ => NewPalette());

    private static PlayableTextPalette NewPalette() => new ();
}
