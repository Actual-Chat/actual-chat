namespace ActualChat.Internal;

internal sealed class LanguageHandle(Symbol id, Symbol shortTitle, string title)
{
    internal static readonly LanguageHandle None = new("", "?", "Unknown");

    public readonly Symbol Id = id;
    public readonly Symbol ShortTitle = shortTitle;
    public readonly string Title = title;
}
