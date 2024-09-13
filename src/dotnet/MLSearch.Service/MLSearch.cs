using ActualChat.Chat;
using ActualChat.Users;

namespace ActualChat.MLSearch;

public class MLSearchImpl (ICommander commander, IMLSearchBackend backend, IChats chats, IAccounts accounts): IMLSearch
{
    private IChats Chats => chats;
    private IAccounts Accounts => accounts;
    private IMLSearchBackend Backend { get; } = backend;

    private ICommander Commander { get; } = commander;

    public virtual async Task<string> GetIndexDocIdByEntryId(
        Session session,
        ChatEntryId chatEntryId,
        CancellationToken cancellationToken)
    {
        if (session.Id.IsEmpty)
            throw new ArgumentOutOfRangeException(nameof(session));

        if (chatEntryId.IsNone)
            throw new ArgumentOutOfRangeException(nameof(chatEntryId));

        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        account.Require(AccountFull.MustBeAdmin);

        var chat = await Chats.Get(session, chatEntryId.ChatId, cancellationToken).Require().ConfigureAwait(false);
        chat.Rules.Require(ChatPermissions.Read);

        return await Backend.GetIndexDocIdByEntryId(chatEntryId, cancellationToken).ConfigureAwait(false);
    }

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
        var chat = await Commander.Call(
            chatChangeCommand,
            isOutermost: true,
            cancellationToken: cancellationToken
        ).ConfigureAwait(false);
        // ---
        var upsertCommand = new AuthorsBackend_Upsert(
            chat.Id,
            default,
            Constants.User.Sherlock.UserId,
            null,
            new AuthorDiff()
        );
        var botAuthor = await Commander.Call(upsertCommand, isOutermost: true, cancellationToken).ConfigureAwait(false);
        var promoteCommand = new Authors_PromoteToOwner(command.Session, botAuthor.Id);
        _ = await Commander.Call(promoteCommand, isOutermost: true, cancellationToken).ConfigureAwait(false);
        return new MLSearchChat(chat.Id);
    }
}
