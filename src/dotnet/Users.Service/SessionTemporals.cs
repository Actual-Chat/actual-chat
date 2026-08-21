namespace ActualChat.Users;

public class SessionTemporals(IServiceProvider services) : ISessionTemporals
{
    private IServiceProvider Services { get; } = services;
    private ISessionTemporalsBackend Backend { get; } = services.GetRequiredService<ISessionTemporalsBackend>();
    private ICommander Commander { get; } = services.Commander();
    private ILogger Log => field ??= Services.LogFor(GetType());

    // [ComputeMethod]
    public virtual async Task<string?> Get(Session session, string key, CancellationToken cancellationToken)
    {
        var value = await Backend.Get(session, ToReadKey(key), cancellationToken).ConfigureAwait(false);
        // Log.LogWarning("Get: {Session}/{Key} = {Value}", session, key, value ?? "null");
        return value;
    }

    // [CommandHandler]
    public virtual async Task OnSet(SessionTemporals_Set command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var session = command.Session;
        var key = command.Key;
        var value = command.Value;
        var backendCommand = new SessionTemporalsBackend_Set(session, ToWriteKey(key, value), value);
        await Commander.Call(backendCommand, true, cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private static string ToReadKey(string key)
        => Constants.SessionTemporals.IsServerKey(key)
            ? key
            : Constants.SessionTemporals.ToClientKey(key);

    private static string ToWriteKey(string key, string? value)
    {
        // A client may dismiss a server key in its own session, but never write a value into one
        var isDismissal = value is null && Constants.SessionTemporals.IsServerKey(key);
        return isDismissal ? key : Constants.SessionTemporals.ToClientKey(key);
    }
}
