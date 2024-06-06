using System.Reflection.Metadata.Ecma335;
using ActualChat.Chat;

namespace ActualChat.MLSearch;

internal class MLSearchImpl (ICommander commander): IMLSearch
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
        Symbol SystemBotTag = Constants.Chat.SystemTags.Bot;
        long botLocalId = Constants.User.MLSearchBot.AuthorLocalId;
        const string searchTitle = "Search";
        var chatChange = Change.Create<ChatDiff> (new() {
            IsPublic = false,
            Title = searchTitle,
            Kind = ChatKind.Group,
            // TODO:
            MediaId = default,

            AllowGuestAuthors = null,
            AllowAnonymousAuthors = null,
            IsTemplate = null,
            TemplateId = Option<ChatId?>.None,
            TemplatedForUserId = Option<UserId?>.None,
            SystemTag = SystemBotTag,
        });
        var chatChangeCommand = new Chats_Change(command.Session, ChatId.None, null, chatChange);
        var chat = await commander.Call(
            chatChangeCommand, 
            isOutermost: true, 
            cancellationToken: cancellationToken
        ).ConfigureAwait(false);
        // ---
        UserId mlBotUserId = Constants.User.MLSearchBot.UserId;
        var upsertCommand = new AuthorsBackend_Upsert(
            chat.Id, default, mlBotUserId, null,
            new AuthorDiff() {
                IsAnonymous = false,
                HasLeft = false,
                AvatarId = null,
            }
        );
        var botAuthor = await commander.Call(upsertCommand, isOutermost: true, cancellationToken).ConfigureAwait(false);
        // ---
        var promoteCommand = new Authors_PromoteToOwner(command.Session, botAuthor.Id);
        var __promoteResult = await commander.Call(promoteCommand, isOutermost: true, cancellationToken).ConfigureAwait(false);
        return new MLSearchChat(chat.Id);
    }
}
