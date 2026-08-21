#pragma warning disable CS0618 // Type or member is obsolete

namespace ActualChat.Contacts;

/// <summary>
/// Frontend service for managing external contacts (phone contacts) with session-based access control.
/// </summary>
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
        if (Invalidation.IsActive)
            return null!; // It just spawns other commands, so nothing to do here

        var session = command.Session;
        var changes = command.Changes;
        if (changes.Length > ExternalContacts_BulkChange.MaxChangeCount)
            throw StandardError.Constraint(
                $"A contact batch cannot contain more than {ExternalContacts_BulkChange.MaxChangeCount} changes.");

        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (!account.IsActive())
            return Enumerable.Repeat(new Result<ExternalContactFull?>(null, null), changes.Length).ToArray();

        foreach (var itemChange in changes) {
            var (id, _, change) = itemChange;
            id.Require();
            change.RequireValid();
            if (id.UserDeviceId.OwnerId != account.Id)
                throw Unauthorized();

            if (change.Update.IsSome(out var update) || change.Create.IsSome(out update))
                RequireValidContact(update);
        }

        var bulkChangeCommand = new ExternalContactsBackend_BulkChange(changes);
        return await Commander.Call(bulkChangeCommand, true, cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private static void RequireValidContact(ExternalContactFull contact)
    {
        contact.DisplayName.RequireMaxLength(ExternalContacts_BulkChange.MaxNameLength);
        contact.GivenName.RequireMaxLength(ExternalContacts_BulkChange.MaxNameLength);
        contact.FamilyName.RequireMaxLength(ExternalContacts_BulkChange.MaxNameLength);
        contact.MiddleName.RequireMaxLength(ExternalContacts_BulkChange.MaxNameLength);
        contact.NamePrefix.RequireMaxLength(ExternalContacts_BulkChange.MaxNameLength);
        contact.NameSuffix.RequireMaxLength(ExternalContacts_BulkChange.MaxNameLength);
        RequireValidHashes(contact.PhoneHashes);
        RequireValidHashes(contact.EmailHashes);
    }

    private static void RequireValidHashes(ApiSet<string> hashes)
    {
        if (hashes.Count > ExternalContacts_BulkChange.MaxHashCount)
            throw StandardError.Constraint(
                $"A contact cannot have more than {ExternalContacts_BulkChange.MaxHashCount} hashes of one kind.");

        foreach (var hash in hashes)
            hash.RequireMaxLength(ExternalContacts_BulkChange.MaxHashLength);
    }

    private static Exception Unauthorized()
        => StandardError.Unauthorized("You can access only your own external contacts.");
}
