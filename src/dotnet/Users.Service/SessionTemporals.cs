namespace ActualChat.Users;

public class SessionTemporals(IServiceProvider services) : ISessionTemporals
{
    private ISessionTemporalsBackend Backend { get; } = services.GetRequiredService<ISessionTemporalsBackend>();
    private ICommander Commander { get; } = services.Commander();

    // [ComputeMethod]
    public virtual Task<string?> Get(Session session, string key, CancellationToken cancellationToken)
        => Backend.Get(session, key, cancellationToken);

    // [CommandHandler]
    public virtual async Task OnSet(SessionTemporals_Set command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var backendCommand = new SessionTemporalsBackend_Set(command.Session, command.Key, command.Value);
        await Commander.Call(backendCommand, true, cancellationToken).ConfigureAwait(false);
    }
}
