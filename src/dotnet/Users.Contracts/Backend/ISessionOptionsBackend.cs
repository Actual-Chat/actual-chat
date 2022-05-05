namespace ActualChat.Users;

public interface ISessionOptionsBackend
{
    // Commands

    [CommandHandler]
    Task Upsert(UpsertCommand command, CancellationToken cancellationToken);

    [DataContract]
    public record UpsertCommand(
        [property: DataMember] Session Session,
        [property: DataMember] KeyValuePair<string, string> Option
        ) : ISessionCommand<Unit>, IBackendCommand;
}
