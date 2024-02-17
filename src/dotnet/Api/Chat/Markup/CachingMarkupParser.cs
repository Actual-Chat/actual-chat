namespace ActualChat.Chat;

public class CachingMarkupParser(IMarkupParser parser, ILruCache<string, Markup> cache) : IMarkupParser
{
    public Markup Parse(string text)
    {
        if (text.IsNullOrEmpty())
            return Markup.Empty;

        if (cache.TryGetValue(text, out var markup))
            return markup;

        markup = parser.Parse(text);
        cache.TryAdd(text, markup);
        return markup;
    }
}
