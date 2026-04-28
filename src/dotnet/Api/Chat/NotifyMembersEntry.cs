namespace ActualChat.Chat;

/// <summary>
/// System entry emitted when an author asks for the attention of all members.
/// </summary>
[DataContract, MessagePackObject(AllowPrivate = true)]
public sealed partial record NotifyMembersEntry : SystemEntry
{
    [DataMember, Key(20)] public AuthorId TargetAuthorId { get; init; } = default!;
    [DataMember, Key(21)] public string TargetAuthorName { get; init; } = "";

    public NotifyMembersEntry() : base((ChatEntryId)null!) { }

    [SerializationConstructor]
    public NotifyMembersEntry(ChatEntryId id, long version = 0) : base(id, version) { }

    public override Markup ToMarkup()
    {
        var authorMentionId = MentionId.NewAuthor(TargetAuthorId);
        var authorName = TargetAuthorName.NullIfEmpty() ?? "Someone";
        return new MarkupSeq(
            new MentionMarkup(authorMentionId, authorName),
            new PlainTextMarkup(" asked for attention."));
    }
}
