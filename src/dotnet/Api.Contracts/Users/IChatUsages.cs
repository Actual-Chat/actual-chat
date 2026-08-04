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
public sealed partial record ChatUsages_RegisterUsage(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] ChatUsageListKind Kind,
    [property: DataMember, Key(2)] ChatId ChatId,
    [property: DataMember, Key(3)] DateTime? AccessTime = null
) : ISessionCommand<Unit>, IApiCommand;
