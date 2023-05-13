namespace ActualChat.Chat.UI.Blazor.Services;

[DataContract]
public sealed record ChatListSettings(
    [property: DataMember] ChatListOrder Order = ChatListOrder.ByLastEventTime,
    [property: DataMember] Symbol FilterId = default
)
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ChatListFilter Filter => ChatListFilter.Parse(FilterId);
}
