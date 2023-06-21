using MemoryPack;

namespace ActualChat.Users;

public interface IAccounts : IComputeService
{
    [ComputeMethod(MinCacheDuration = 60)]
    Task<AccountFull> GetOwn(Session session, CancellationToken cancellationToken);
    [ComputeMethod(MinCacheDuration = 60)]
    Task<Account?> Get(Session session, UserId userId, CancellationToken cancellationToken);
    [ComputeMethod(MinCacheDuration = 60)]
    Task<AccountFull?> GetFull(Session session, UserId userId, CancellationToken cancellationToken);

    [CommandHandler]
    public Task OnUpdate(Accounts_Update command, CancellationToken cancellationToken);
    [CommandHandler]
    public Task OnInvalidateEverything(Accounts_InvalidateEverything command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record Accounts_Update(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] AccountFull Account,
    [property: DataMember, MemoryPackOrder(2)] long? ExpectedVersion
) : ISessionCommand<Unit>;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record Accounts_InvalidateEverything(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] bool Everywhere = false
) : ISessionCommand<Unit>;
