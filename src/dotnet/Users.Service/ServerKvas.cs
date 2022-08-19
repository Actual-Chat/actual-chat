using ActualChat.Kvas;

namespace ActualChat.Users;

public class ServerKvas : IServerKvas
{
    private IAuth Auth { get; }
    private IServerKvasBackend Backend { get; }
    private ICommander Commander { get; }

    public ServerKvas(IAuth auth, IServerKvasBackend backend, ICommander commander)
    {
        Auth = auth;
        Backend = backend;
        Commander = commander;
    }

    // [ComputeMethod]
    public virtual async Task<Option<string>> Get(Session session, string key, CancellationToken cancellationToken = default)
    {
        var prefix = await GetPrefix(session, cancellationToken).ConfigureAwait(false);
        var result = await Backend.Get(prefix, key, cancellationToken).ConfigureAwait(false);
        return result == null ? default : Option<string>.Some(result);
    }

    // [CommandHandler]
    public virtual async Task Set(IServerKvas.SetCommand command, CancellationToken cancellationToken = default)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var prefix = await GetPrefix(command.Session, cancellationToken).ConfigureAwait(false);
        var cmd = new IServerKvasBackend.SetManyCommand(prefix, new[] { (command.Key, command.Value) });
        await Commander.Call(cmd, true, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task SetMany(IServerKvas.SetManyCommand command, CancellationToken cancellationToken = default)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var prefix = await GetPrefix(command.Session, cancellationToken).ConfigureAwait(false);
        var cmd = new IServerKvasBackend.SetManyCommand(prefix, command.Items.Select(i => (i.Key, i.Value)).ToArray());
        await Commander.Call(cmd, true, cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private async ValueTask<string> GetPrefix(Session session, CancellationToken cancellationToken)
    {
        var user = await Auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        return user != null
            ? $"u/{user.Id.Value}/"
            : $"s/{session.Id.Value}/";
    }
}
