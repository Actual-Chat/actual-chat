namespace ActualChat.Users;

/// <summary>
/// Service for tracking user read and view positions in chats.
/// </summary>
public interface IChatPositions : IComputeService
{
    [ComputeMethod, RemoteComputeMethod(MinCacheDuration = 600)]
    Task<ChatPosition> GetOwn(Session session, ChatId chatId, ChatPositionKind kind, CancellationToken cancellationToken);

    [CommandHandler]
    Task OnSet(ChatPositions_Set command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatPositions_Set(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), Key(1)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(2), Key(2)] ChatPositionKind Kind,
    [property: DataMember, MemoryPackOrder(3), Key(3)] ChatPosition Position
) : ISessionCommand<Unit>, IApiCommand;
