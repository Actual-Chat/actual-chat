namespace ActualChat.Internal;

internal sealed class LanguageHandle
{
    internal static readonly LanguageHandle None = new("", "?", "Unknown");

    public readonly Symbol Id;
    public readonly Symbol Code;
    public readonly string Title;

    public LanguageHandle(Symbol id, Symbol code, string title)
    {
        Id = id;
        Code = code;
        Title = title;
    }
}
