using ActualChat.Contacts;
using ActualChat.Users;

namespace ActualChat.Chat;

public class ChatMigrator(IServiceProvider services) : IChatMigrator
{
    private IChats Chats { get; } = services.GetRequiredService<IChats>();
    private IPlaces Places { get; } = services.GetRequiredService<IPlaces>();
    private IContacts Contacts { get; } = services.GetRequiredService<IContacts>();
    private ICommander Commander { get; } = services.GetRequiredService<ICommander>();
    private ILogger Log { get; } = services.LogFor<ChatMigrator>();

    // [CommandHandler]
    public virtual async Task<bool> OnMoveToPlace(ChatMigrator_MoveChatToPlace command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return default; // It just spawns other commands, so nothing to do here

        var (session, chatId, placeId) = command;
        var executed = false;
        Log.LogInformation("ChatMigrator_MoveChatToPlace: starting, moving chat '{ChatId}' to place '{PlaceId}'", chatId.Value, placeId);
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        Log.LogInformation("Chat for chat id '{ChatId}' is {Chat}", chatId, chat);
        if (chat != null) {
            if (chat.Id.Kind != ChatKind.Group)
                throw StandardError.Constraint("Only group chats can be moved to a Place.");
            if (!chat.Rules.IsOwner())
                throw StandardError.Constraint("You must be the Owner of this chat to perform the migration.");

            var place = await Places.Get(session, placeId, cancellationToken).Require().ConfigureAwait(false);
            if (!place.Rules.IsOwner())
                throw StandardError.Constraint("You should be a place owner to perform 'move to place' operation.");

            var backendCmd = new ChatMigratorBackend_MoveChatToPlace(chatId, placeId);
            await Commander.Call(backendCmd, true, cancellationToken).ConfigureAwait(false);
            executed = true;
        }

        var placeChatId = new PlaceChatId(PlaceChatId.Format(placeId, chatId.Id));
        var newChatId = (ChatId)placeChatId;
        var contact = await Contacts.GetForChat(session, newChatId, cancellationToken).ConfigureAwait(false);
        Log.LogInformation("Contact for chat id '{ChatId}' is {Contact}", newChatId, contact);
        if (contact == null || contact.PlaceId.IsNone || !contact.IsStored()) {
            var backendCmd2 = new ContactMigratorBackend_MoveChatToPlace(chatId, placeId);
            await Commander.Call(backendCmd2, true, cancellationToken).ConfigureAwait(false);
            executed = true;
        }

        {
            var backendCmd3 = new UserMigratorBackend_MoveChatToPlace(chatId, placeId);
            var hasChanges = await Commander.Call(backendCmd3, true, cancellationToken).ConfigureAwait(false);
            Log.LogInformation("UserMigratorBackend_MoveChatToPlace: completed");
            executed |= hasChanges;
        }

        Log.LogInformation("ChatMigrator_MoveChatToPlace: completed");
        return executed;
    }
}
