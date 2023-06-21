using MemoryPack;

namespace ActualChat.Users;

public interface IChatPositions : IComputeService
{
    [ComputeMethod]
    Task<ChatPosition> GetOwn(Session session, ChatId chatId, ChatPositionKind kind, CancellationToken cancellationToken);

    [CommandHandler]
    Task OnSet(ChatPositions_Set command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatPositions_Set(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(2)] ChatPositionKind Kind,
    [property: DataMember, MemoryPackOrder(3)] ChatPosition Position
) : ISessionCommand<Unit>;
