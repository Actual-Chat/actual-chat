namespace ActualChat.Users;

public interface ISessionTemporals : IComputeService
{
    [ComputeMethod]
    Task<string?> Get(Session session, string key, CancellationToken cancellationToken);

    [CommandHandler]
    Task OnSet(SessionTemporals_Set command, CancellationToken cancellationToken);
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record SessionTemporals_Set(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] string Key,
    [property: DataMember, Key(2)] string? Value
) : ISessionCommand<Unit>, IApiCommand;
