namespace ActualChat.Chat;

/// <summary>
/// Query parameters for listing changed chats by version range.
/// </summary>
[DataContract, MessagePackObject]
public partial record ChangedChatsQuery
{
    [DataMember, Key(2)] public required ChatId? LastId { get; init; }
    [DataMember, Key(3)] public required int Limit { get; init; }
    [DataMember, Key(0)] public long MinVersion { get; init; }
    [DataMember, Key(1)] public long MaxVersion { get; init; } = long.MaxValue;
    [DataMember, Key(4)] public bool ExcludePeerChats { get; init; }
    [DataMember, Key(5)] public bool ExcludePlaceRootChats { get; init; }
}
