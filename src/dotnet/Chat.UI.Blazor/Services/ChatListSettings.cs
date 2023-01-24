namespace ActualChat.Chat.UI.Blazor.Services;

[DataContract]
public sealed record ChatListSettings(
    [property: DataMember] ChatListOrder Order = ChatListOrder.ByOwnUpdateTime,
    [property: DataMember] Symbol FilterId = default
)
{
    public ChatListFilter Filter => ChatListFilter.Parse(FilterId);
}
