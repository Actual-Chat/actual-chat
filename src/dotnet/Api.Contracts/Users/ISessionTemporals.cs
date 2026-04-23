namespace ActualChat.Users;

public interface ISessionTemporals : IComputeService
{
    [ComputeMethod]
    Task<string?> Get(Session session, string key, CancellationToken cancellationToken);

    [CommandHandler]
    Task OnSet(SessionTemporals_Set command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record SessionTemporals_Set(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), Key(1)] string Key,
    [property: DataMember, MemoryPackOrder(2), Key(2)] string? Value
) : ISessionCommand<Unit>, IApiCommand;
