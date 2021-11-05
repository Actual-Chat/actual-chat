namespace ActualChat.Users;

public interface ISessionOptionsBackend
{
    // Commands

    [CommandHandler]
    Task Update(UpdateCommand command, CancellationToken cancellationToken);

    public record UpdateCommand(
            Session Session,
            KeyValuePair<string, string> Option
        ) : ISessionCommand<Unit>, IBackendCommand;
}
