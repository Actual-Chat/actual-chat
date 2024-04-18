using ActualChat.Kvas;

namespace ActualChat.Users;

public class ServerKvas : IServerKvas
{
    private IAuth Auth { get; }
    private IServerKvasBackend Backend { get; }
    private ICommander Commander { get; }
    private MomentClockSet Clocks { get; }
    private ILogger Log { get; }

    public ServerKvas(IServiceProvider services)
    {
        Log = services.LogFor(GetType());
        Clocks = services.Clocks();
        Auth = services.GetRequiredService<IAuth>();
        Backend = services.GetRequiredService<IServerKvasBackend>();
        Commander = services.Commander();
    }

    // [ComputeMethod]
    public virtual async Task<byte[]?> Get(Session session, string key, CancellationToken cancellationToken = default)
    {
        var prefix = await GetPrefix(session, cancellationToken).ConfigureAwait(false);
        return await Backend.Get(prefix, key, cancellationToken).ConfigureAwait(false);

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
    public virtual async Task OnSet(ServerKvas_Set command, CancellationToken cancellationToken = default)
    {
        var (session, key, value) = command;
        var prefix = await GetPrefix(session, cancellationToken).ConfigureAwait(false);
        var setManyCommand = new ServerKvasBackend_SetMany(prefix, (key, value));
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
    public virtual async Task OnSetMany(ServerKvas_SetMany command, CancellationToken cancellationToken = default)
    {
        var (session, items) = command;
        var backendItems = items.Select(i => (i.Key, i.Value)).ToArray();
        var prefix = await GetPrefix(session, cancellationToken).ConfigureAwait(false);
        var setManyCommand = new ServerKvasBackend_SetMany(prefix, backendItems);
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
    public virtual async Task OnMigrateGuestKeys(ServerKvas_MigrateGuestKeys command, CancellationToken cancellationToken = default)
    {
        var session = command.Session;

        // This piece is tricky: since this command is started while auth info isn't committed yet,
        // it's not guaranteed that GetUserPrefix will complete w/ a non-empty one here.
        // But it should complete with a non-empty one eventually, so...
        try {
            await Clocks.Timeout(3).ApplyTo(
                ct => Computed
                    .Capture(() => Auth.GetUser(session, ct), ct)
                    .When(u => u?.IsGuest() == false, ct),
                cancellationToken
                ).ConfigureAwait(false);
        }
        catch (TimeoutException) {
            Log.LogWarning("MigrateGuestKeys: Auth.GetUser couldn't complete in 3 seconds");
            return;
        }

        var userPrefix = await GetUserPrefix(session, cancellationToken).ConfigureAwait(false);
        if (userPrefix == null) {
            Log.LogWarning("MigrateGuestKeys: GetUserPrefix(...) == null");
            return;
        }

        var guestPrefix = await GetGuestPrefix(session, cancellationToken).ConfigureAwait(false);
        if (guestPrefix.IsNullOrEmpty())
            return;

        await TryMigrateKeys(guestPrefix, userPrefix!, cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private async ValueTask<string> GetPrefix(Session session, CancellationToken cancellationToken)
        => await GetUserPrefix(session, cancellationToken).ConfigureAwait(false)
            ?? await GetGuestPrefix(session, cancellationToken).ConfigureAwait(false)
            ?? "";

    private async ValueTask<string?> GetUserPrefix(Session session, CancellationToken cancellationToken)
    {
        var user = await Auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        return user == null ? null : ServerKvasBackendExt.GetUserPrefix(new UserId(user.Id));
    }

    private async ValueTask<string?> GetGuestPrefix(Session session, CancellationToken cancellationToken)
    {
        var sessionInfo = await Auth.GetSessionInfo(session, cancellationToken).ConfigureAwait(false);
        var guestId = sessionInfo.GetGuestId();
        return guestId.IsNone ? null : ServerKvasBackendExt.GetUserPrefix(guestId);
    }

    private async ValueTask<Dictionary<string, byte[]>?> TryMigrateKeys(
        string fromPrefix,
        string toPrefix,
        CancellationToken cancellationToken)
    {
        var keys = await Backend.List(fromPrefix, cancellationToken).ConfigureAwait(false);
        if (keys.Count == 0) {
            Log.LogInformation("TryMigrateKeys: nothing to migrate");
            return null;
        }

        Dictionary<string, byte[]> movedKeys;
        HashSet<string> skippedKeys;
        using (Computed.SuspendDependencyCapture()) {
            movedKeys = new Dictionary<string, byte[]>(StringComparer.Ordinal) {
                { Kvas.KvasExt.MigratedKey, KvasSerializer.SerializedTrue },
            };
            skippedKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (key, value) in keys) {
                var userValue = await Backend.Get(toPrefix, key, cancellationToken).ConfigureAwait(false);
                if (userValue == null)
                    movedKeys[key] = value;
                else
                    skippedKeys.Add(key);
            }
        }

        Log.LogInformation("TryMigrateKeys: {FromPrefix} -> {ToPrefix}, move {MoveKeys}, skip {SkipKeys}",
            fromPrefix, toPrefix,
            movedKeys.Keys.OrderBy(x => x, StringComparer.Ordinal).ToDelimitedString(),
            skippedKeys.OrderBy(x => x, StringComparer.Ordinal).ToDelimitedString());

        // Create missing keys in toPrefix
        var createMissingKeysCommand = new ServerKvasBackend_SetMany(toPrefix,
            movedKeys.Select(kv => (kv.Key, (byte[]?) kv.Value)).ToArray());
        await Commander.Call(createMissingKeysCommand, true, cancellationToken).ConfigureAwait(false);

        // Remove all keys in fromPrefix
        var removeOldKeysCommand = new ServerKvasBackend_SetMany(fromPrefix,
            keys.Select(kv => (kv.Key, (byte[]?) null)).ToArray());
        await Commander.Call(removeOldKeysCommand, true, cancellationToken).ConfigureAwait(false);

        return movedKeys;
    }
}
