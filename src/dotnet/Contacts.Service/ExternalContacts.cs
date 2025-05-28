using ActualChat.Users;
#pragma warning disable CS0618 // Type or member is obsolete

namespace ActualChat.Contacts;

public class ExternalContacts(IServiceProvider services) : IExternalContacts
{
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IExternalContactsBackend Backend { get; } = services.GetRequiredService<IExternalContactsBackend>();
    private ICommander Commander { get; } = services.Commander();

    // [ComputeMethod]
    public virtual async Task<ExternalContact[]> List(
        Session session,
        Symbol deviceId,
        CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (!account.IsActive())
            return [];

        return await Backend.List(UserDeviceId.New(account.Id, deviceId), cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<Result<ExternalContactFull?>[]> OnBulkChange(
        ExternalContacts_BulkChange command, CancellationToken cancellationToken)
    {
        var (session, changes) = command;
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (!account.IsActive())
            return Enumerable.Repeat(new Result<ExternalContactFull?>(null, null), changes.Length).ToArray();

        foreach (var itemChange in changes) {
            var (id, _, change) = itemChange;
            id.Require();
            change.RequireValid();
            if (id.UserDeviceId.OwnerId != account.Id)
                throw Unauthorized();
        }

        var bulkChangeCommand = new ExternalContactsBackend_BulkChange(changes);
        return await Commander.Call(bulkChangeCommand, true, cancellationToken).ConfigureAwait(false);
    }

    private static Exception Unauthorized()
        => StandardError.Unauthorized("You can access only your own external contacts.");
}
