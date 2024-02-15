using MemoryPack;

namespace ActualChat.AiSearch;

// Represents either entire chat history or its tale since the last reset point
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class AiSearchChatHistory
{
    private AiSearchChatMessage[]? _messages;

    [DataMember(Order = 0), MemoryPackOrder(0)]
    public AiSearchChatMessage[] Messages {
        get => _messages ?? [];
        init => _messages = value;
    }
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record AiSearchChatMessage(
    [property: DataMember, MemoryPackOrder(0)] ChatEntryId Id,
    [property: DataMember, MemoryPackOrder(1)] Moment EndsAt,
    [property: DataMember, MemoryPackOrder(2)] string Role,
    [property: DataMember, MemoryPackOrder(3)] string Text
)
{}
