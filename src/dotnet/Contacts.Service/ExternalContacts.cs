using ActualChat.Users;
#pragma warning disable CS0618 // Type or member is obsolete

namespace ActualChat.Contacts;

public class ExternalContacts(IServiceProvider services) : IExternalContacts
{
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IExternalContactsBackend Backend { get; } = services.GetRequiredService<IExternalContactsBackend>();
    private ICommander Commander { get; } = services.Commander();

    // [ComputeMethod]
    [Obsolete("2024.04: Replaced with List2")]
    public virtual async Task<ApiArray<ExternalContactFull>> List(Session session, Symbol deviceId, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
       if (!account.IsActive())
            return default;

        return await Backend.ListFull(account.Id, deviceId, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<ExternalContact>> List2(
        Session session,
        Symbol deviceId,
        CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (!account.IsActive())
            return ApiArray<ExternalContact>.Empty;

        return await Backend.List(new UserDeviceId(account.Id, deviceId), cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    [Obsolete("2023.10: Replaced with OnBulkChange.")]
    public virtual async Task<ExternalContactFull?> OnChange(ExternalContacts_Change command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return default!; // It just spawns other commands, so nothing to do here

        var (session, id, expectedVersion, change) = command;
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (!account.IsActive())
            return null;

        id.Require();
        change.RequireValid();

        if (id.UserDeviceId.OwnerId != account.Id)
            throw Unauthorized();

        var results = await Commander
            .Call(new ExternalContactsBackend_BulkChange(ApiArray.New(new ExternalContactChange(id, expectedVersion, change))), cancellationToken)
            .ConfigureAwait(false);
        if (results[0].Error is { } error)
            throw error;

        return results[0].Value;
    }

    // [CommandHandler]
    public virtual async Task<ApiArray<Result<ExternalContactFull?>>> OnBulkChange(ExternalContacts_BulkChange command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return default!; // It just spawns other commands, so nothing to do here

        var (session, changes) = command;
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (!account.IsActive())
            return Enumerable.Repeat(new Result<ExternalContactFull?>(null, null), changes.Count).ToApiArray();

        foreach (var itemChange in changes) {
            var (id, _, change) = itemChange;
            id.Require();
            change.RequireValid();
            if (id.UserDeviceId.OwnerId != account.Id)
                throw Unauthorized();
        }

        return await Commander
            .Call(new ExternalContactsBackend_BulkChange(changes), cancellationToken)
            .ConfigureAwait(false);
    }

    private static Exception Unauthorized()
        => StandardError.Unauthorized("You can access only your own external contacts.");
}
