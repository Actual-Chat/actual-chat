namespace ActualChat.Chat;

/// <summary>
/// Service for observing and updating locations shared into a chat (live or frozen one-shot).
/// </summary>
public interface ISharedLocations : IComputeService
{
    [ComputeMethod]
    Task<SharedLocation?> Get(Session session, ChatId chatId, SharedLocationId id, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ApiArray<SharedLocation>> ListLive(Session session, ChatId chatId, CancellationToken cancellationToken);

    [CommandHandler]
    Task<SharedLocation?> OnChange(SharedLocations_Change command, CancellationToken cancellationToken);
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record SharedLocations_Change(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] ChatId ChatId,
    [property: DataMember, Key(2)] SharedLocationId? Id,
    [property: DataMember, Key(3)] Change<SharedLocationDiff> Change
) : ISessionCommand<SharedLocation?>, IApiCommand;
