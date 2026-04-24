namespace ActualChat.Chat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record ChatListSettings(
    [property: DataMember, MemoryPackOrder(0), Key(0)] ChatListOrder Order = ChatListOrder.ByLastEventTime,
    [property: DataMember, MemoryPackOrder(1), Key(1)] Symbol FilterId = default
) : StoredSettings
{
    public static readonly ChatListSettings None = new ();

    public static string GetKvasKey(string placeId) => $"ChatListSettings({placeId})";
}
