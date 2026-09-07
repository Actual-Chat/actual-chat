using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

/// <summary>
/// Wire-frozen v2.7 SystemEntry wrapper. The current shape used <see cref="IUnionRecord{T}"/>
/// with typed accessor properties; this record preserves that layout for old clients.
/// </summary>
[DataContract]
[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed partial record LegacySystemEntry : IUnionRecord<LegacySystemEntryOption?>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember]
    public LegacySystemEntryOption? Option { get; init; }

    [DataMember]
    public LegacyMembersChangedOption? MembersChanged {
        get => Option as LegacyMembersChangedOption;
        init => Option ??= value;
    }

    [DataMember]
    public LegacyNotifyMembersOption? NotifyMembers {
        get => Option as LegacyNotifyMembersOption;
        init => Option ??= value;
    }

    [DataMember]
    public LegacyCallOption? Call {
        get => Option as LegacyCallOption;
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
        CallEntry c => new LegacySystemEntry {
            Option = new LegacyCallOption(
                c.CallerId, c.CallerName, c.Outcome, c.InviteeIds.ToArray(), c.HasVideo),
        },
        _ => null,
    };
}

public abstract record LegacySystemEntryOption;

[DataContract]
public sealed partial record LegacyMembersChangedOption : LegacySystemEntryOption
{
    [DataMember] public AuthorId? AuthorId { get; init; }
    [DataMember] public string AuthorName { get; init; } = "";
    [DataMember] public bool HasLeft { get; init; }

    [JsonConstructor, Newtonsoft.Json.JsonConstructor]
    public LegacyMembersChangedOption(AuthorId? authorId, string authorName, bool hasLeft)
    {
        AuthorId = authorId;
        AuthorName = authorName;
        HasLeft = hasLeft;
    }
}

[DataContract]
public sealed partial record LegacyNotifyMembersOption : LegacySystemEntryOption
{
    [DataMember] public AuthorId? AuthorId { get; init; }
    [DataMember] public string AuthorName { get; init; } = "";

    [JsonConstructor, Newtonsoft.Json.JsonConstructor]
    public LegacyNotifyMembersOption(AuthorId? authorId, string authorName)
    {
        AuthorId = authorId;
        AuthorName = authorName;
    }
}

[DataContract]
public sealed partial record LegacyCallOption : LegacySystemEntryOption
{
    [DataMember] public AuthorId CallerId { get; init; } = null!;
    [DataMember] public string CallerName { get; init; } = "";
    [DataMember] public CallOutcome Outcome { get; init; }
    [DataMember] public AuthorId[] InviteeIds { get; init; } = [];
    [DataMember] public bool HasVideo { get; init; }

    [JsonConstructor, Newtonsoft.Json.JsonConstructor]
    public LegacyCallOption(
        AuthorId callerId, string callerName, CallOutcome outcome, AuthorId[] inviteeIds, bool hasVideo)
    {
        CallerId = callerId;
        CallerName = callerName;
        Outcome = outcome;
        InviteeIds = inviteeIds;
        HasVideo = hasVideo;
    }
}
