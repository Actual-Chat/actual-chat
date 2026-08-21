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
public sealed partial record SessionTemporals_Set : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required string Key { get; init; }
    [DataMember(Order = 3), Key(3)] public required string? Value { get; init; }
}
