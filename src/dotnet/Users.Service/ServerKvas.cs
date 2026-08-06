using ActualChat.Kvas;

namespace ActualChat.Users;

/// <summary>
/// Frontend service for server-side key-value storage with session-based access control.
/// </summary>
public class ServerKvas(IServiceProvider services) : IServerKvas
{
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IServerKvasBackend Backend { get; } = services.GetRequiredService<IServerKvasBackend>();
    private ICommander Commander { get; } = services.Commander();
    private MomentClockSet Clocks { get; } = services.Clocks();
    private ILogger Log { get; } = services.LogFor<ServerKvas>();

    // [ComputeMethod]
    public virtual async Task<byte[]?> Get(Session session, string key, CancellationToken cancellationToken = default)
    {
        if (KvasKeys.IsHidden(key))
            return null;

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
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var (session, key, value) = command;
        RequireValidItem(key, value);
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
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var (session, items) = command;
        var maxItemCount = ServerKvas_SetMany.MaxItemCount;
        if (items.Length > maxItemCount)
            throw StandardError.Constraint($"A batch cannot contain more than {maxItemCount} items.");

        foreach (var (key, value) in items)
            RequireValidItem(key, value);
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
    public virtual async Task OnMigrateGuestKeys(
        ServerKvas_MigrateGuestKeys command,
        CancellationToken cancellationToken = default)
    {
        // Nothing dispatches this command anymore: its last user was invite activation, which now
        // requires a signed-in account. Kept for now in case guest-to-user hand-off comes back -
        // if it does, TryMigrateKeys must start skipping KvasKeys.HiddenPrefix keys, since
        // moving those across an identity boundary would move access grants with them.
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var session = command.Session;

        // This piece is tricky: since this command is started while auth info isn't committed yet,
        // it's not guaranteed that GetUserPrefix will complete w/ a non-empty one here.
        // But it should complete with a non-empty one eventually, so...
        try {
            await Clocks.Timeout(3).ApplyTo(
                async ct => {
                    var c = await Computed.Capture(() => Accounts.GetOwn(session, ct), ct).ConfigureAwait(false);
                    return await c.When(account => !account.IsGuest, ct).ConfigureAwait(false);
                },
                cancellationToken
                ).ConfigureAwait(false);
        }
        catch (TimeoutException) {
            Log.LogWarning("MigrateGuestKeys: Accounts.GetOwn couldn't complete in 3 seconds");
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
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        return !account.IsGuest
            ? UserScopedKvasBackend.GetUserPrefix(account.Id)
            : null;
    }

    private async ValueTask<string?> GetGuestPrefix(Session session, CancellationToken cancellationToken)
    {
        var sessionInfo = await Accounts.GetSessionInfo(session, cancellationToken).ConfigureAwait(false);
        var guestId = sessionInfo.GetGuestId();
        return guestId is null ? null : UserScopedKvasBackend.GetUserPrefix(guestId);
    }

    // Unused - see OnMigrateGuestKeys, its only caller
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
        using (Computed.BeginIsolation()) {
            movedKeys = new Dictionary<string, byte[]> {
                { Kvas.KvasExt.MigratedKey, KvasSerializer.SerializedTrue },
            };
            skippedKeys = new HashSet<string>();
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
            movedKeys.Keys.OrderBy(x => x).ToDelimitedString(),
            skippedKeys.OrderBy(x => x).ToDelimitedString());

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

    private static void RequireValidItem(string key, byte[]? value)
    {
        KvasKeys.RequireNotHidden(key);
        key.RequireMaxLength(ServerKvas_Set.MaxKeyLength);
        if (value is { } v && v.Length > ServerKvas_Set.MaxValueLength)
            throw StandardError.Constraint("Value is too big.");
    }
}
