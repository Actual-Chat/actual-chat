using ActualChat.ScheduledCommands;

namespace ActualChat.Chat.Events;

[DataContract]
public record TextEntryChangedEvent(
    [property: DataMember(Order = 0)]
    string ChatId,
    [property: DataMember(Order = 1)]
    long EntryId,
    [property: DataMember(Order = 2)]
    string AuthorId,
    [property: DataMember(Order = 3)]
    string Content,
    [property: DataMember(Order = 4)]
    EntryState State) : IEvent;

public enum EntryState
{
    Removed = 0,
    New = 1,
    Updated = 2,
}
