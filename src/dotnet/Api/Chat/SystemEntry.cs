using ActualLab.Fusion.Blazor;
using MemoryPack;

namespace ActualChat.Chat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed partial record SystemEntry : IUnionRecord<SystemEntryOption?>
{
    // Union options
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public SystemEntryOption? Option { get; init; }

    [DataMember, MemoryPackOrder(0)]
    public MembersChangedOption? MembersChanged {
        get => Option as MembersChangedOption;
        init => Option ??= value;
    }

    [DataMember, MemoryPackOrder(1)]
    public NotifyMembersOption? NotifyMembers {
        get => Option as NotifyMembersOption;
        init => Option ??= value;
    }

    public static implicit operator SystemEntry(SystemEntryOption option) => new() { Option = option };

    // This record relies on referential equality
    public bool Equals(SystemEntry? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}

public abstract record SystemEntryOption : IRequirementTarget
{
    public abstract Markup ToMarkup();
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record MembersChangedOption : SystemEntryOption
{
    [DataMember, MemoryPackOrder(0)] public AuthorId? AuthorId { get; init; }
    [DataMember, MemoryPackOrder(1)] public string AuthorName { get; init; } = "";
    [DataMember, MemoryPackOrder(2)] public bool HasLeft { get; init; }

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
    public MembersChangedOption(AuthorId? authorId, string authorName, bool hasLeft)
    {
        AuthorId = authorId;
        AuthorName = authorName;
        HasLeft = hasLeft;
    }

    public override Markup ToMarkup()
    {
        var authorName = AuthorName.NullIfEmpty() ?? "Someone";
        var verb = HasLeft ? "left" : "joined";
        return AuthorId is null
            ? new PlainTextMarkup($"{authorName} has {verb} the chat.")
            : new MarkupSeq(
                new MentionMarkup(MentionId.NewAuthor(AuthorId), authorName),
                new PlainTextMarkup($" has {verb} the chat."));
    }
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record NotifyMembersOption : SystemEntryOption
{
    [DataMember, MemoryPackOrder(0)] public AuthorId AuthorId { get; init; }
    [DataMember, MemoryPackOrder(1)] public string AuthorName { get; init; } = "";

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
    public NotifyMembersOption(AuthorId authorId, string authorName)
    {
        AuthorId = authorId;
        AuthorName = authorName;
    }

    public override Markup ToMarkup()
    {
        var authorMentionId = MentionId.NewAuthor(AuthorId);
        var authorName = AuthorName.NullIfEmpty() ?? "Someone";
        return new MarkupSeq(
            new MentionMarkup(authorMentionId, authorName),
            new PlainTextMarkup(" asked for attention."));
    }
}
