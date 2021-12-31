namespace ActualChat.Users;

public interface ISessionOptionsBackend
{
    // Commands

    [CommandHandler]
    Task Upsert(UpsertCommand command, CancellationToken cancellationToken);

    public record UpsertCommand(
            Session Session,
            KeyValuePair<string, string> Option
        ) : ISessionCommand<Unit>, IBackendCommand;
}
