using ActualChat.Chat;
using ActualChat.Contacts.Db;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Contacts;

public class ContactsMigrationBackend(IServiceProvider services) : DbServiceBase<ContactsDbContext>(services), IContactsMigrationBackend
{
    private IContactsBackend ContactsBackend { get; } = services.GetRequiredService<IContactsBackend>();

    public virtual async Task OnMoveChatToPlace(ContactsMigrationBackend_MoveChatToPlace command, CancellationToken cancellationToken)
    {
        var (chatId, placeId) = command;

        if (Computed.IsInvalidating()) {
            if (ContactsBackend is ContactsBackend contactsBackend) {
                _ = contactsBackend.PseudoChatContact(chatId);
                _ = contactsBackend.PseudoPlaceContact(placeId);
            }
            return;
        }

        var chatSid = chatId.Value;
        var placeSid = placeId.Value;
        var placeChatId = new PlaceChatId(PlaceChatId.Format(placeId, chatId.Id));
        var newChatId = (ChatId)placeChatId;

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

            var updateCount = await dbContext.Contacts
                .Where(c => c.Id == contactSid)
                .ExecuteUpdateAsync(setters => setters
                        .SetProperty(c => c.Id, c => newContactSid)
                        .SetProperty(c => c.ChatId, c => chatSid)
                        .SetProperty(c => c.PlaceId, c => placeSid),
                    cancellationToken)
                .ConfigureAwait(false);
            AssertOneUpdated(updateCount);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Place contacts should be added on adding authors to place root chat during processing ChatsMigrationBackend_MoveToPlace.
    }

    private static void AssertOneUpdated(int updateCount)
        => AssertXUpdated(updateCount, 1);

    private static void AssertXUpdated(int updateCount, int expectedUpdateCount)
    {
        if (updateCount != expectedUpdateCount)
            throw new InvalidOperationException($"Expected {expectedUpdateCount} row should be update, but {updateCount} rows were updated.");
    }
}
