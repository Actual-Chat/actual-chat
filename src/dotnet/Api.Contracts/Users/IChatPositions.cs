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

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatPositions_Set(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] ChatId ChatId,
    [property: DataMember, Key(2)] ChatPositionKind Kind,
    [property: DataMember, Key(3)] ChatPosition Position
) : ISessionCommand<Unit>, IApiCommand;
