using MemoryPack;

namespace ActualChat.Chat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record SystemEntry : IUnionRecord<SystemEntryOption?>
{
    // Union options
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, MemoryPackIgnore]
    public SystemEntryOption? Option { get; init; }

    [DataMember, MemoryPackOrder(0)]
    public MembersChangedOption? MembersChanged {
        get => Option as MembersChangedOption;
        init => Option ??= value;
    }

    public static implicit operator SystemEntry(SystemEntryOption option) => new() { Option = option };
}

public abstract record SystemEntryOption : IRequirementTarget
{
    public abstract Markup ToMarkup();
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record MembersChangedOption : SystemEntryOption
{
    [DataMember, MemoryPackOrder(0)] public AuthorId AuthorId { get; init; }
    [DataMember, MemoryPackOrder(1)] public string AuthorName { get; init; } = "";
    [DataMember, MemoryPackOrder(2)] public bool HasLeft { get; init; }

    public MembersChangedOption() { }

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
    public MembersChangedOption(AuthorId authorId, string authorName, bool hasLeft)
    {
        AuthorId = authorId;
        AuthorName = authorName;
        HasLeft = hasLeft;
    }

    public override Markup ToMarkup()
    {
        var authorMentionId = new MentionId(AuthorId, AssumeValid.Option);
        var authorName = AuthorName.NullIfEmpty() ?? "Someone";
        var verb = HasLeft ? "left" : "joined";
        return new MarkupSeq(
            new MentionMarkup(authorMentionId, authorName),
            new PlainTextMarkup($" has {verb} the chat."));
    }
}
