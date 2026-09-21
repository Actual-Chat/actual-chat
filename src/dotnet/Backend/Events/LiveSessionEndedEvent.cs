using ActualChat.Live;

namespace ActualChat;

/// <summary>
/// Fired once when a latched live session closes, with everyone who took part in it.
/// </summary>
[DataContract, MessagePackObject(true)]
public partial record LiveSessionEndedEvent(
    [property: DataMember] ChatId ChatId,
    [property: DataMember] Moment StartedAt,
    [property: DataMember] Moment EndedAt,
    [property: DataMember] LiveSessionKind Kind,
    [property: DataMember] ApiArray<LiveSessionEndedMember> Members
) : EventCommand, IHasShardKey
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => ChatId.ShardKey;
}

[DataContract, MessagePackObject(true)]
public sealed partial record LiveSessionEndedMember(
    [property: DataMember] AuthorId AuthorId,
    [property: DataMember] Moment JoinedAt
);
