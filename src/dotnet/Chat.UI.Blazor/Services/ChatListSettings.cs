using MemoryPack;

namespace ActualChat.Chat.UI.Blazor.Services;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record ChatListSettings(
    [property: DataMember, MemoryPackOrder(0)] ChatListOrder Order = ChatListOrder.ByLastEventTime,
    [property: DataMember, MemoryPackOrder(1)] Symbol FilterId = default
)
{
    public static readonly ChatListSettings None = new ();

    public static string GetKvasKey(string placeId) => $"ChatListSettings({placeId})";

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public ChatListFilter Filter => ChatListFilter.Parse(FilterId);
}
