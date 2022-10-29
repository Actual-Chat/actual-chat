using ActualChat.Kvas;

namespace ActualChat.Users;

public class ServerKvas : IServerKvas
{
    private IAuth Auth { get; }
    private IServerKvasBackend Backend { get; }
    private ICommander Commander { get; }

    public ServerKvas(IServiceProvider services)
    {
        Auth = services.GetRequiredService<IAuth>();
        Backend = services.GetRequiredService<IServerKvasBackend>();
        Commander = services.Commander();
    }

    // [ComputeMethod]
    public virtual async Task<Option<string>> Get(Session session, string key, CancellationToken cancellationToken = default)
    {
        var prefix = await GetPrefix(session, cancellationToken).ConfigureAwait(false);
        var result = await Backend.Get(prefix, key, cancellationToken).ConfigureAwait(false);
        return result == null ? default : Option<string>.Some(result);

        // More complex logic that moves session keys on demand
        /*
        var userPrefix = await GetUserPrefix(session, cancellationToken).ConfigureAwait(false);
        string? result;
        if (userPrefix == null) {
            // No user, so we can only use sessionPrefix
            var sessionPrefix = GetSessionPrefix(session);
            result = await Backend.Get(sessionPrefix, key, cancellationToken).ConfigureAwait(false);
        }
        else {
            // Let's hit the user prefix first
            result = await Backend.Get(userPrefix, key, cancellationToken).ConfigureAwait(false);
            if (result == null) {
                // No result - let's try to move every missing key from sessionPrefix
                var sessionPrefix = GetSessionPrefix(session);
                var movedKeys = await TryMoveToUser(sessionPrefix, userPrefix, cancellationToken).ConfigureAwait(false);
                result = movedKeys?.GetValueOrDefault(key);
            }
        }
        return result == null ? default : Option<string>.Some(result);
        */
    }

    // [CommandHandler]
    public virtual async Task Set(IServerKvas.SetCommand command, CancellationToken cancellationToken = default)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var (session, key, value) = command;
        var prefix = await GetPrefix(session, cancellationToken).ConfigureAwait(false);
        var setManyCommand = new IServerKvasBackend.SetManyCommand(prefix, (key, value));
        await Commander.Call(setManyCommand, true, cancellationToken).ConfigureAwait(false);

        // More complex logic that moves session keys on demand
        /*
        var userPrefix = await GetUserPrefix(session, cancellationToken).ConfigureAwait(false);
        var sessionPrefix = GetSessionPrefix(session);
        if (userPrefix == null) {
            var cmd = new IServerKvasBackend.SetManyCommand(sessionPrefix, (key, value));
            await Commander.Call(cmd, true, cancellationToken).ConfigureAwait(false);
        }
        else {
            await TryMoveToUser(sessionPrefix, userPrefix, cancellationToken).ConfigureAwait(false);
            var cmd = new IServerKvasBackend.SetManyCommand(userPrefix, (key, value));
            await Commander.Call(cmd, true, cancellationToken).ConfigureAwait(false);
        }
        */
    }

    // [CommandHandler]
    public virtual async Task SetMany(IServerKvas.SetManyCommand command, CancellationToken cancellationToken = default)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var (session, items) = command;
        var backendItems = items.Select(i => (i.Key, i.Value)).ToArray();
        var prefix = await GetPrefix(session, cancellationToken).ConfigureAwait(false);
        var setManyCommand = new IServerKvasBackend.SetManyCommand(prefix, backendItems);
        await Commander.Call(setManyCommand, true, cancellationToken).ConfigureAwait(false);

        // More complex logic that moves session keys on demand
        /*
        var userPrefix = await GetUserPrefix(session, cancellationToken).ConfigureAwait(false);
        var sessionPrefix = GetSessionPrefix(session);
        if (userPrefix == null) {
            var cmd = new IServerKvasBackend.SetManyCommand(sessionPrefix, backendItems);
            await Commander.Call(cmd, true, cancellationToken).ConfigureAwait(false);
        }
        else {
            await TryMoveToUser(sessionPrefix, userPrefix, cancellationToken).ConfigureAwait(false);
            var cmd = new IServerKvasBackend.SetManyCommand(userPrefix, backendItems);
            await Commander.Call(cmd, true, cancellationToken).ConfigureAwait(false);
        }
        */
    }

    // [CommandHandler]
    public virtual async Task MoveSessionKeys(IServerKvas.MoveSessionKeysCommand command, CancellationToken cancellationToken = default)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var session = command.Session;
        var userPrefix = await GetUserPrefix(session, cancellationToken).ConfigureAwait(false);
        if (userPrefix == null)
            return;

        var sessionPrefix = GetSessionPrefix(session);
        await TryMoveToUser(sessionPrefix, userPrefix, cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private async ValueTask<string> GetPrefix(Session session, CancellationToken cancellationToken)
        => await GetUserPrefix(session, cancellationToken).ConfigureAwait(false)
            ?? GetSessionPrefix(session);

    private async ValueTask<string?> GetUserPrefix(Session session, CancellationToken cancellationToken)
    {
        var user = await Auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        return user != null ? Backend.GetUserPrefix(user.Id) : null;
    }

    private string GetSessionPrefix(Session session)
        => Backend.GetSessionPrefix(session);

    private async ValueTask<Dictionary<string, string>?> TryMoveToUser(
        string sessionPrefix,
        string userPrefix,
        CancellationToken cancellationToken)
    {
        var sessionKeys = await Backend.List(sessionPrefix, cancellationToken).ConfigureAwait(false);
        if (sessionKeys.Count == 0)
            return null;

        using var _ = Computed.SuspendDependencyCapture();
        var movedKeys = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in sessionKeys) {
            var userValue = await Backend.Get(userPrefix, key, cancellationToken).ConfigureAwait(false);
            if (userValue == null)
                movedKeys[key] = value;
        }

        // Create missing keys in userPrefix
        await Commander.Call(
            new IServerKvasBackend.SetManyCommand(userPrefix, movedKeys.Select(kv => (kv.Key, (string?) kv.Value)).ToArray()),
            true, cancellationToken
        ).ConfigureAwait(false);
        // Remove all keys in sessionPrefix
        await Commander.Call(
            new IServerKvasBackend.SetManyCommand(sessionPrefix, sessionKeys.Select(kv => (kv.Key, (string?) null)).ToArray()),
            true, cancellationToken
        ).ConfigureAwait(false);

        return movedKeys;
    }
}
