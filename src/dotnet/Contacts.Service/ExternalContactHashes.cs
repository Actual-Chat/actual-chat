using ActualChat.Users;

namespace ActualChat.Contacts;

public class ExternalContactHashes(IAccounts accounts, IExternalContactHashesBackend backend, ICommander commander) : IExternalContactHashes
{
    // [ComputeMethod]
    public virtual async Task<ExternalContactsHash?> Get(
        Session session,
        Symbol deviceId,
        CancellationToken cancellationToken)
    {
        var account = await accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (!account.IsActive())
            return default;

        return await backend.Get(new UserDeviceId(account.Id, deviceId), cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<ExternalContactsHash?> OnChange(
        ExternalContactHashes_Change command,
        CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return default!; // It just spawns other commands, so nothing to do here

        var (session, deviceId, expectedVersion, change) = command;
        var account = await accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (!account.IsActive())
            return null;

        deviceId.Require();
        return await commander
            .Call(new ExternalContactHashesBackend_Change(new UserDeviceId(account.Id, deviceId), expectedVersion, change), cancellationToken)
            .ConfigureAwait(false);
    }
}
