using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

/// <summary>
/// Wire-frozen v2.7 SystemEntry wrapper. The current shape used <see cref="IUnionRecord{T}"/>
/// with typed accessor properties; this record preserves that layout for old clients.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed partial record LegacySystemEntry : IUnionRecord<LegacySystemEntryOption?>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public LegacySystemEntryOption? Option { get; init; }

    [DataMember, MemoryPackOrder(0)]
    public LegacyMembersChangedOption? MembersChanged {
        get => Option as LegacyMembersChangedOption;
        init => Option ??= value;
    }

    [DataMember, MemoryPackOrder(1)]
    public LegacyNotifyMembersOption? NotifyMembers {
        get => Option as LegacyNotifyMembersOption;
        init => Option ??= value;
    }

    public static implicit operator LegacySystemEntry(LegacySystemEntryOption option)
        => new() { Option = option };

    public bool Equals(LegacySystemEntry? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);

    public static LegacySystemEntry? From(ChatEntry entry) => entry switch {
        MembersChangedEntry mc => new LegacySystemEntry {
            Option = new LegacyMembersChangedOption(mc.TargetAuthorId, mc.TargetAuthorName, mc.HasLeft),
        },
        NotifyMembersEntry nm => new LegacySystemEntry {
            Option = new LegacyNotifyMembersOption(nm.TargetAuthorId, nm.TargetAuthorName),
        },
        _ => null,
    };
}

public abstract record LegacySystemEntryOption;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record LegacyMembersChangedOption : LegacySystemEntryOption
{
    [DataMember, MemoryPackOrder(0)] public AuthorId? AuthorId { get; init; }
    [DataMember, MemoryPackOrder(1)] public string AuthorName { get; init; } = "";
    [DataMember, MemoryPackOrder(2)] public bool HasLeft { get; init; }

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
    public LegacyMembersChangedOption(AuthorId? authorId, string authorName, bool hasLeft)
    {
        AuthorId = authorId;
        AuthorName = authorName;
        HasLeft = hasLeft;
    }
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record LegacyNotifyMembersOption : LegacySystemEntryOption
{
    [DataMember, MemoryPackOrder(0)] public AuthorId? AuthorId { get; init; }
    [DataMember, MemoryPackOrder(1)] public string AuthorName { get; init; } = "";

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
    public LegacyNotifyMembersOption(AuthorId? authorId, string authorName)
    {
        AuthorId = authorId;
        AuthorName = authorName;
    }
}
