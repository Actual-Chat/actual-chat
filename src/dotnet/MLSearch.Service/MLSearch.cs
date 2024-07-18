using ActualChat.Chat;

namespace ActualChat.MLSearch;

public class MLSearchImpl (ICommander commander): IMLSearch
{
    public virtual async Task<MLSearchChat> OnCreate(MLSearch_CreateChat command, CancellationToken cancellationToken)
    {
        // This method is called from the client side
        // It creates a new ML search chat with two participants:
        //  - The user who initiated the search
        //  - An Assistant
        // --
        // Note: Quick workaround to make a chat owned by a bot.
        // Promote ownership instead of creating it by the bot from the start.
        // Reason: not sure how to create a session for a bot.
        var chatChange = Change.Create<ChatDiff> (new() {
            IsPublic = false,
            Title = command.Title,
            Kind = ChatKind.Group,
            MediaId = command.MediaId,
            SystemTag = Constants.Chat.SystemTags.Bot,
        });
        var chatChangeCommand = new Chats_Change(command.Session, ChatId.None, null, chatChange);
        var chat = await commander.Call(
            chatChangeCommand,
            isOutermost: true,
            cancellationToken: cancellationToken
        ).ConfigureAwait(false);
        // ---
        var upsertCommand = new AuthorsBackend_Upsert(
            chat.Id,
            default,
            Constants.User.MLSearchBot.UserId,
            null,
            new AuthorDiff()
        );
        var botAuthor = await commander.Call(upsertCommand, isOutermost: true, cancellationToken).ConfigureAwait(false);
        var promoteCommand = new Authors_PromoteToOwner(command.Session, botAuthor.Id);
        _ = await commander.Call(promoteCommand, isOutermost: true, cancellationToken).ConfigureAwait(false);
        return new MLSearchChat(chat.Id);
    }
}
