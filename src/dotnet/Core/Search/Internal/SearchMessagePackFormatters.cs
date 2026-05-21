using MessagePack.Formatters;

namespace ActualChat.Search.Internal;

public sealed class SearchMatchMessagePackFormatter : IMessagePackFormatter<SearchMatch?>
{
    public void Serialize(ref MessagePackWriter writer, SearchMatch? value, MessagePackSerializerOptions options)
    {
        if (value is null) { writer.WriteNil(); return; }
        writer.WriteMapHeader(3);
        writer.Write(nameof(SearchMatch.Text));
        options.Resolver.GetFormatterWithVerify<string>().Serialize(ref writer, value.Text, options);
        writer.Write(nameof(SearchMatch.Rank));
        writer.Write(value.Rank);
        writer.Write(nameof(SearchMatch.Parts));
        options.Resolver.GetFormatterWithVerify<SearchMatchPart[]>().Serialize(ref writer, value.Parts, options);
    }

    public SearchMatch? Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        if (reader.TryReadNil())
            return null;
        var mapLen = reader.ReadMapHeader();
        var text = "";
        var rank = 0d;
        SearchMatchPart[] parts = [];
        for (var i = 0; i < mapLen; i++) {
            var key = reader.ReadString();
            switch (key) {
                case nameof(SearchMatch.Text):
                    text = options.Resolver.GetFormatterWithVerify<string>().Deserialize(ref reader, options);
                    break;
                case nameof(SearchMatch.Rank):
                    rank = reader.ReadDouble();
                    break;
                case nameof(SearchMatch.Parts):
                    parts = options.Resolver.GetFormatterWithVerify<SearchMatchPart[]>().Deserialize(ref reader, options) ?? [];
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }
        return new SearchMatch(text!, rank, parts);
    }
}

public sealed class SearchMatchPartMessagePackFormatter : IMessagePackFormatter<SearchMatchPart?>
{
    public void Serialize(ref MessagePackWriter writer, SearchMatchPart? value, MessagePackSerializerOptions options)
    {
        if (value is null) { writer.WriteNil(); return; }
        writer.WriteMapHeader(2);
        writer.Write(nameof(SearchMatchPart.Range));
        options.Resolver.GetFormatterWithVerify<Range<int>>().Serialize(ref writer, value.Range, options);
        writer.Write(nameof(SearchMatchPart.Rank));
        writer.Write(value.Rank);
    }

    public SearchMatchPart? Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        if (reader.TryReadNil())
            return null;
        var mapLen = reader.ReadMapHeader();
        Range<int> range = default;
        var rank = 0d;
        for (var i = 0; i < mapLen; i++) {
            var key = reader.ReadString();
            switch (key) {
                case nameof(SearchMatchPart.Range):
                    range = options.Resolver.GetFormatterWithVerify<Range<int>>().Deserialize(ref reader, options);
                    break;
                case nameof(SearchMatchPart.Rank):
                    rank = reader.ReadDouble();
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }
        return new SearchMatchPart(range, rank);
    }
}

public sealed class SearchPhraseMessagePackFormatter : IMessagePackFormatter<SearchPhrase?>
{
    public void Serialize(ref MessagePackWriter writer, SearchPhrase? value, MessagePackSerializerOptions options)
    {
        if (value is null) { writer.WriteNil(); return; }
        writer.WriteMapHeader(2);
        writer.Write(nameof(SearchPhrase.Terms));
        options.Resolver.GetFormatterWithVerify<string[]>().Serialize(ref writer, value.Terms, options);
        writer.Write(nameof(SearchPhrase.MatchPrefixes));
        writer.Write(value.MatchPrefixes);
    }

    public SearchPhrase? Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        if (reader.TryReadNil())
            return null;
        var mapLen = reader.ReadMapHeader();
        string[] terms = [];
        var matchPrefixes = false;
        for (var i = 0; i < mapLen; i++) {
            var key = reader.ReadString();
            switch (key) {
                case nameof(SearchPhrase.Terms):
                    terms = options.Resolver.GetFormatterWithVerify<string[]>().Deserialize(ref reader, options) ?? [];
                    break;
                case nameof(SearchPhrase.MatchPrefixes):
                    matchPrefixes = reader.ReadBoolean();
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }
        return new SearchPhrase(terms, matchPrefixes);
    }
}
