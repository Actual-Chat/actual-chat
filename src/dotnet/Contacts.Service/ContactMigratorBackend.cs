using ActualChat.Contacts.Db;
using ActualChat.Db;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Contacts;

public class ContactMigratorBackend(IServiceProvider services) : DbServiceBase<ContactsDbContext>(services), IContactMigratorBackend
{
    private IContactsBackend ContactsBackend { get; } = services.GetRequiredService<IContactsBackend>();

    public virtual async Task OnMoveChatToPlace(ContactMigratorBackend_MoveChatToPlace command, CancellationToken cancellationToken)
    {
        var (chatId, placeId) = command;
        var placeChatId = new PlaceChatId(PlaceChatId.Format(placeId, chatId.Id));
        var newChatId = (ChatId)placeChatId;

        if (Computed.IsInvalidating()) {
            if (ContactsBackend is ContactsBackend contactsBackend) {
                _ = contactsBackend.Internals.PseudoChatContact(chatId);
                _ = contactsBackend.Internals.PseudoChatContact(newChatId);
                _ = contactsBackend.Internals.PseudoPlaceContact(placeId);
            }
            return;
        }

        Log.LogInformation("ContactMigratorBackend_MoveChatToPlace: starting, moving chat '{ChatId}' to place '{PlaceId}'", chatId.Value, placeId);

        var chatSid = chatId.Value;
        var placeSid = placeId.Value;

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbContacts = await dbContext.Contacts
            .Where(c => c.ChatId == chatSid)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Update DbContacts
        foreach (var dbContact in dbContacts) {
            var contactSid = dbContact.Id;
            var ownerId = new UserId(dbContact.OwnerId);
            var newContactSid = ContactId.Format(ownerId, newChatId);
            var newChatSid = newChatId.Value;

            _ = await dbContext.Contacts
                .Where(c => c.Id == contactSid)
                .ExecuteUpdateAsync(setters => setters
                        .SetProperty(c => c.Id, _ => newContactSid)
                        .SetProperty(c => c.ChatId, _ => newChatSid)
                        .SetProperty(c => c.PlaceId, _ => placeSid),
                    cancellationToken)
                .RequireOneUpdated()
                .ConfigureAwait(false);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Place contacts should be added on adding authors to place root chat during processing ChatsMigrationBackend_MoveToPlace.

        Log.LogInformation("ContactMigratorBackend_MoveChatToPlace: completed");
    }
}
