namespace ActualChat.Chat.Events;

[DataContract]
public record NewChatEntryEvent(
    [property:DataMember(Order = 0)]string ChatId,
    [property:DataMember(Order = 1)]long Id,
    [property:DataMember(Order = 2)]string Content): IChatEvent;
