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
        var value = await Backend.Get(session, key, cancellationToken).ConfigureAwait(false);
        // Log.LogWarning("Get: {Session}/{Key} = {Value}", session, key, value ?? "null");
        return value;
    }

    // [CommandHandler]
    public virtual async Task OnSet(SessionTemporals_Set command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var backendCommand = new SessionTemporalsBackend_Set(command.Session, command.Key, command.Value);
        await Commander.Call(backendCommand, true, cancellationToken).ConfigureAwait(false);
    }
}
