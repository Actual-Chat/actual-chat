namespace ActualChat.Users;

public interface ISessionOptionsBackend
{
    /// <summary>
    /// Updates the session object to store data from the <paramref name="command"/>
    /// </summary>
    [CommandHandler, Internal]
    Task Update(UpdateCommand command, CancellationToken cancellationToken);

    // Commands

    /// <summary>
    /// Create or update value inside of current <seealso cref="SessionInfo.Options"/>
    /// </summary>
    public record UpdateCommand(
            Session Session,
            KeyValuePair<string, string> Option
        ) : BackendCommand<Unit>, ISessionCommand<Unit>;
}
