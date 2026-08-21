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
public sealed partial record SharedLocations_Change : ApiCommand<SharedLocation?>
{
    [DataMember(Order = 2), Key(2)] public required ChatId ChatId { get; init; }
    [DataMember(Order = 3), Key(3)] public required SharedLocationId? Id { get; init; }
    [DataMember(Order = 4), Key(4)] public required Change<SharedLocationDiff> Change { get; init; }
}
