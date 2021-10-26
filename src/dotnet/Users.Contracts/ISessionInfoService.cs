namespace ActualChat.Users;

public interface ISessionInfoService
{
    /// <summary>
    /// Updates the session object to store data from the <paramref name="command"/>
    /// </summary>
    [CommandHandler, Internal]
    Task Update(UpsertData command, CancellationToken cancellationToken);

    /// <summary>
    /// Create or update value inside of current <seealso cref="SessionInfo.Options"/>
    /// </summary>
    public record UpsertData(Session Session, KeyValuePair<string, string> KeyValue) : ISessionCommand<Unit>;
}