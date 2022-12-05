namespace ActualChat.Internal;

internal sealed class LanguageHandle
{
    internal static readonly LanguageHandle None = new("", "?", "Unknown");

    public readonly Symbol Id;
    public readonly Symbol Shortcut;
    public readonly string Title;

    public LanguageHandle(Symbol id, Symbol shortcut, string title)
    {
        Id = id;
        Shortcut = shortcut;
        Title = title;
    }
}
