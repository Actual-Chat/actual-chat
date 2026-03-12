namespace ActualChat.Chat;

/// <summary>
/// Caching decorator for <see cref="IMarkupParser"/> using LRU cache.
/// </summary>
public class CachingMarkupParser(IMarkupParser parser, ILruCache<string, Markup> cache) : IMarkupParser
{
    public Markup Parse(string text)
    {
        if (text.IsNullOrEmpty())
            return MarkupParser.EmptyResult;

        if (cache.TryGetValue(text, out var markup))
            return markup;

        markup = parser.Parse(text);
        cache.TryAdd(text, markup);
        return markup;
    }
}
