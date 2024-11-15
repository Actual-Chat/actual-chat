using ActualLab.Versioning;
using MemoryPack;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

public sealed record SimpleItem(
    [property: DataMember(Order = 0), MemoryPackOrder(0)] ChatId Id,
    [property: DataMember(Order = 2), MemoryPackOrder(1)] string Content,
    [property: DataMember(Order = 1), MemoryPackOrder(1)] long Version = 0
) : IHasId<ChatId>, IHasVersion<long>;
