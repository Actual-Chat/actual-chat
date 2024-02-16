using MemoryPack;

namespace ActualChat.Users;

public interface IAccounts : IComputeService
{
    [ComputeMethod(MinCacheDuration = 60), ClientComputeMethod(ClientCacheMode = ClientCacheMode.Cache)]
    Task<AccountFull> GetOwn(Session session, CancellationToken cancellationToken);
    [ComputeMethod(MinCacheDuration = 60), ClientComputeMethod(ClientCacheMode = ClientCacheMode.Cache)]
    Task<Account?> Get(Session session, UserId userId, CancellationToken cancellationToken);
    [ComputeMethod(MinCacheDuration = 60)]
    Task<AccountFull?> GetFull(Session session, UserId userId, CancellationToken cancellationToken);

    [CommandHandler]
    public Task OnUpdate(Accounts_Update command, CancellationToken cancellationToken);

    [CommandHandler]
    public Task OnDeleteOwn(Accounts_DeleteOwn command, CancellationToken cancellationToken);
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
public sealed partial record Accounts_DeleteOwn(
    [property: DataMember, MemoryPackOrder(0)] Session Session
) : ISessionCommand<Unit>;
