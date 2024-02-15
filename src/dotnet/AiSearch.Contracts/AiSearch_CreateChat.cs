using MemoryPack;

namespace ActualChat.AiSearch;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record AiSearch_CreateChat(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] AiSearchChatId AiSearchChatId,
    [property: DataMember, MemoryPackOrder(2)] long? ExpectedVersion
) : ISessionCommand<AiSearchChat>;
