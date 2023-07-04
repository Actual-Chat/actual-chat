using ActualChat.Commands;
using MemoryPack;

namespace ActualChat.Chat.Events;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record ChatChangedEvent(
    [property: DataMember, MemoryPackOrder(1)] Chat Chat,
    [property: DataMember, MemoryPackOrder(2)] Chat? OldChat
) : EventCommand;
