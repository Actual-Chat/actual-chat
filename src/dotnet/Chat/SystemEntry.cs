namespace ActualChat.Chat;

[DataContract]
public record SystemEntry : IUnionRecord<SystemEntryOption?>
{
    // Union options
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public SystemEntryOption? Option { get; init; }

    [DataMember]
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

[DataContract]
public record MembersChangedOption(
    [property: DataMember] AuthorId AuthorId,
    [property: DataMember] string AuthorName,
    [property: DataMember] bool HasLeft
    ) : SystemEntryOption
{
    [Obsolete("This constructor is used to deserialize legacy MembersChangedOption w/o AuthorName property.")]
    public MembersChangedOption() : this(default, "", false) { }

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
