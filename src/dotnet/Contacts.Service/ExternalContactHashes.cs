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

        return await backend.Get(UserDeviceId.New(account.Id, deviceId), cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<ExternalContactsHash?> OnChange(
        ExternalContactHashes_Change command,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return null!; // It just spawns other commands, so nothing to do here

        var (session, deviceId, expectedVersion, change) = command;
        var account = await accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (!account.IsActive())
            return null;

        deviceId.Require();
        var changeCommand = new ExternalContactHashesBackend_Change(UserDeviceId.New(account.Id, deviceId), expectedVersion, change);
        return await commander.Call(changeCommand, true, cancellationToken).ConfigureAwait(false);
    }
}
