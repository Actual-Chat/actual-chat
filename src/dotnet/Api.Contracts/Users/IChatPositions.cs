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

// Not deduplicated: the read-position writer debounces at 1s and builds a fresh Uuid each time,
// so dedup could never replay one - it would only hold an entry per scroll.

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatPositions_Set : ApiCommand<Unit>, INotDeduplicated
{
    [DataMember(Order = 2), Key(2)] public required ChatId ChatId { get; init; }
    [DataMember(Order = 3), Key(3)] public required ChatPositionKind Kind { get; init; }
    [DataMember(Order = 4), Key(4)] public required ChatPosition Position { get; init; }
}
