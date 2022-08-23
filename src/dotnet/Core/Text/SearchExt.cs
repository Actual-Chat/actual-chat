namespace ActualChat;

public static class SearchExt
{
    public static SearchPhrase ToSearchPhrase(this string text)
        => new(text);
}
