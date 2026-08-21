namespace ActualChat.Users;

/// <summary>
/// Service for tracking recent chat usage patterns.
/// </summary>
public interface IChatUsages : IComputeService
{
    [ComputeMethod]
    Task<ChatId[]> GetRecencyList(Session session, ChatUsageListKind kind, CancellationToken cancellationToken);

    [CommandHandler]
    Task OnRegisterUsage(ChatUsages_RegisterUsage command, CancellationToken cancellationToken);
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatUsages_RegisterUsage : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required ChatUsageListKind Kind { get; init; }
    [DataMember(Order = 3), Key(3)] public required ChatId ChatId { get; init; }
    [DataMember(Order = 4), Key(4)] public DateTime? AccessTime { get; init; }
}
