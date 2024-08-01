using MemoryPack;

namespace ActualChat.Users;

public interface IChatUsages : IComputeService
{
    [ComputeMethod]
    Task<ApiArray<ChatId>> GetRecencyList(Session session, ChatUsageListKind kind, CancellationToken cancellationToken);

    [CommandHandler]
    Task OnRegisterUsage(ChatUsages_RegisterUsage command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatUsages_RegisterUsage(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] ChatUsageListKind Kind,
    [property: DataMember, MemoryPackOrder(2)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(3)] DateTime? AccessTime = null
) : ISessionCommand<Unit>, IApiCommand;
