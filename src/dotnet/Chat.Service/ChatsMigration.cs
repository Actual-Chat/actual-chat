using ActualChat.Contacts;

namespace ActualChat.Chat;

public class ChatsMigration(IServiceProvider services) : IChatsMigration
{
    private IChats Chats { get; } = services.GetRequiredService<IChats>();
    private IPlaces Places { get; } = services.GetRequiredService<IPlaces>();
    private IContacts Contacts { get; } = services.GetRequiredService<IContacts>();
    private ICommander Commander { get; } = services.GetRequiredService<ICommander>();
    private ILogger Log { get; } = services.LogFor<ChatsMigration>();

    // [CommandHandler]
    public virtual async Task<bool> OnMoveToPlace(ChatsMigration_MoveToPlace command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return default; // It just spawns other commands, so nothing to do here

        var (session, chatId, placeId) = command;
        var executed = false;
        Log.LogInformation("About to perform ChatsMigration_MoveToPlace");
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        Log.LogInformation("Chat for chat id '{ChatId}' is {Chat}", chatId, chat);
        if (chat != null) {
            if (chat.Id.Kind != ChatKind.Group)
                throw StandardError.Constraint("You may perform 'move to place' operation only on a group chat.");
            if (!chat.Rules.IsOwner())
                throw StandardError.Constraint("You should be a chat owner to perform 'move to place' operation.");

            var place = await Places.Get(session, placeId, cancellationToken).Require().ConfigureAwait(false);
            if (!place.Rules.IsOwner())
                throw StandardError.Constraint("You should be a place owner to perform 'move to place' operation.");

            Log.LogInformation("About to perform ChatsMigrationBackend_MoveToPlace");
            var backCommand = new ChatsMigrationBackend_MoveToPlace(chatId, placeId);
            await Commander.Call(backCommand, true, cancellationToken).ConfigureAwait(false);
            Log.LogInformation("Completed ChatsMigrationBackend_MoveToPlace");
            executed = true;
        }

        var placeChatId = new PlaceChatId(PlaceChatId.Format(placeId, chatId.Id));
        var newChatId = (ChatId)placeChatId;
        var contact = await Contacts.GetForChat(session, newChatId, cancellationToken).ConfigureAwait(false);
        Log.LogInformation("Contact for chat id '{ChatId}' is {Contact}", newChatId, contact);
        if (contact == null || contact.PlaceId.IsNone || !contact.IsStored()) {
            Log.LogInformation("About to perform ContactsMigrationBackend_MoveChatToPlace");
            var backCommand2 = new ContactsMigrationBackend_MoveChatToPlace(chatId, placeId);
            await Commander.Call(backCommand2, true, cancellationToken).ConfigureAwait(false);
            Log.LogInformation("Completed ContactsMigrationBackend_MoveChatToPlace");
            executed = true;
        }

        Log.LogInformation("Completed ChatsMigration_MoveToPlace");
        return executed;
    }
}
