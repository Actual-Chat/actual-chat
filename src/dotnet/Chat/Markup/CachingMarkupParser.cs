namespace ActualChat.Chat;

public class CachingMarkupParser : IMarkupParser
{
    private readonly IMarkupParser _parser;
    private readonly ILruCache<string, Markup> _cache;

    public CachingMarkupParser(IMarkupParser parser, ILruCache<string, Markup> cache)
    {
        _parser = parser;
        _cache = cache;
    }

    public Markup Parse(string text)
    {
        if (text.IsNullOrEmpty())
            return Markup.Empty;

        if (_cache.TryGetValue(text, out var markup))
            return markup;

        markup = _parser.Parse(text);
        _cache.TryAdd(text, markup);
        return markup;
    }
}
