namespace ActualChat.Users;

public interface ISessionOptionsBackend : IComputeService
{
    // Commands

    [CommandHandler]
    Task Upsert(UpsertCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record UpsertCommand(
        [property: DataMember] Session Session,
        [property: DataMember] KeyValuePair<string, string> Option
        ) : ISessionCommand<Unit>, IBackendCommand;
}
