using ActualChat.Contacts.Db;
using ActualChat.Users;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Contacts;

public class ContactLinker(IServiceProvider services) : ActivatedWorkerBase(services)
{
    private const int BatchSize = 100;

    private DbHub<ContactsDbContext> DbHub { get; } = services.DbHub<ContactsDbContext>();
    private IAccountsBackend AccountsBackend { get; } = services.GetRequiredService<IAccountsBackend>();
    private IContactsBackend ContactsBackend { get; } = services.GetRequiredService<IContactsBackend>();
    private ICommander Commander { get; } = services.Commander();

    private Tracer Tracer => field ??= Services.TracerFor(GetType());

    protected override async Task<bool> OnActivate(CancellationToken cancellationToken)
    {
        using var _1 = Tracer.MethodRegion();
        var dbContext = await DbHub.CreateDbContext(readWrite: true, cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);
        var dbExternalContactLinks = await dbContext.ExternalContactLinks.ForUpdate()
            .Where(x => !x.IsChecked)
            .OrderBy(x => x.DbExternalContactId)
            .ThenBy(x => x.Value)
            .Take(BatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (dbExternalContactLinks.Count == 0)
            return true;

        using var _2 = Tracer.Region($"Checking {dbExternalContactLinks.Count} external contact link(s)");
        await dbExternalContactLinks
            .GroupBy(c => c.DbExternalContactId)
            .Select(c => EnsureCreated(c.OrderBy(x => x.Value).ToArray()))
            .Collect(HardwareInfo.ProcessorCount * 2, cancellationToken)
            .ConfigureAwait(false);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return false;

        async Task EnsureCreated(DbExternalContactLink[] links)
        {
            // Handle links belonging to the same external contact.
            // The highly likely they should refer to the same user.
            var externalContactId = ExternalContactId.Parse(links[0].DbExternalContactId);
            var ownerId = externalContactId.UserDeviceId.OwnerId;
            var linksPerUserId = new Dictionary<UserId, List<DbExternalContactLink>>();
            foreach (var link in links) {
                var userId = await ExternalContactExt2.FindUser(AccountsBackend, link.Value, cancellationToken).ConfigureAwait(false);
                if (userId is null || userId == ownerId) {
                    link.IsChecked = true;
                    continue;
                }
                if (!linksPerUserId.TryGetValue(userId, out var info)) {
                    info = [];
                    linksPerUserId.Add(userId, info);
                }
                info.Add(link);
            }
            foreach (var (userId, links1) in linksPerUserId) {
                try {
                    await EnsureContactExists(ownerId, userId, cancellationToken).ConfigureAwait(false);
                    // NOTE(DF): Should be mark link as checked even if it failed to create contact?
                    foreach (var link in links1)
                        link.IsChecked = true;
                }
                catch (Exception e) {
                    if (!e.IsCancellationOf(cancellationToken))
                        Log.LogError(e, "Failed to create contact for external contact with id '{ExternalContactId}' and links '{Links}'",
                            externalContactId, LinksAsString(links1));
                    throw;
                }
            }
        }
    }

    private async Task EnsureContactExists(
        UserId ownerId,
        UserId? userId,
        CancellationToken cancellationToken)
    {
        if (userId is null || ownerId == userId)
            return;

        var contactId = ContactId.NewUser(ownerId, userId);
        // check existing contact since command always performs db request
        var contact = await ContactsBackend.Get(ownerId, contactId, cancellationToken).ConfigureAwait(false);
        if (contact.IsBlocked)
            return; // Never revive a blocked contact to Regular
        if (!contact.IsRegular) {
            contact = contact with { State = ContactState.Regular };
            // This command doesn't throw an exception in case contact already exists
            var createCmd = new ContactsBackend_Change(contactId, null, Change.Upsert(contact));
            await Commander.Call(createCmd, cancellationToken).ConfigureAwait(false);
        }

        var reviewCommand = new ContactsBackend_ReviewExternalContactName(contactId);
        _ = Commander.Call(reviewCommand, true, CancellationToken.None).SuppressExceptions();
    }

    private static string LinksAsString(IEnumerable<DbExternalContactLink> links)
        => links.Select(c => c.Value).ToCommaPhrase();
}
