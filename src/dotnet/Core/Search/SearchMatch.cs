namespace ActualChat.Search;

/// <summary>
/// A search match: the matched <see cref="Text"/>, its <see cref="Rank"/>, and the highlight
/// <see cref="Parts"/> — supplied explicitly (server results) or computed lazily from a
/// <see cref="SearchQuery"/> (local results).
/// </summary>
[DataContract, MessagePackObject]
[MessagePackFormatter(typeof(Internal.SearchMatchMessagePackFormatter))]
public sealed partial record SearchMatch
{
    public static readonly SearchMatch Empty = Matchless("");

    private readonly SearchQuery _query;

    [DataMember(Order = 0), Key(0)]
    public string Text { get; }
    [DataMember(Order = 1), Key(1)]
    public double Rank { get; }
    [DataMember(Order = 2), Key(2)]
    public SearchMatchPart[] Parts {
        get => field ??= _query.IsEmpty ? [] : _query.GetMatchParts(Text);
        init;
    }

    [IgnoreDataMember, IgnoreMember]
    public IEnumerable<SearchMatchPart> PartsWithGaps {
        get {
            var lastIndex = 0;
            foreach (var part in Parts) {
                var range = part.Range;
                if (lastIndex < range.Start)
                    yield return new SearchMatchPart((lastIndex, range.Start), 0);

                yield return part;
                lastIndex = range.End;
            }
            if (lastIndex < Text.Length)
                yield return new SearchMatchPart((lastIndex, Text.Length), 0);
        }
    }

    [IgnoreDataMember, IgnoreMember]
    public bool IsMatch => Parts.Length != 0;

    public static SearchMatch Matchless(string text)
        => new(text, 0, []);

    public SearchMatch(string text, SearchMatchPart[] parts)
        : this(text, 0, parts) { }

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, SerializationConstructor]
    public SearchMatch(string text, double rank, SearchMatchPart[] parts)
    {
        Text = text;
        Rank = rank;
        Parts = parts;
    }

    public SearchMatch(string text, SearchQuery query)
        : this(text, 0, query) { }
    public SearchMatch(string text, double rank, SearchQuery query)
    {
        Text = text;
        Rank = rank;
        _query = query;
    }

    public override string ToString()
    {
        var text = Text;
        var parts = Parts.Select(p => p.ToString(text)).ToDelimitedString(", ");
        return $"{GetType().GetName()}(\"{text}\", {Rank:F3}, {{ {parts} }})";
    }
}
