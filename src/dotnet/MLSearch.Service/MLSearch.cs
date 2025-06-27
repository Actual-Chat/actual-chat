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
        ChatEntryId entryId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entryId);

        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        account.Require(AccountFull.MustBeAdmin);

        var chat = await Chats.Get(session, entryId.ChatId, cancellationToken).Require().ConfigureAwait(false);
        chat.Rules.Require(ChatPermissions.Read);

        return await Backend.GetIndexDocIdByEntryId(entryId, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
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

        if (Invalidation.IsActive)
            return null!; // It just spawns other commands, so nothing to do here

        var (session, title, mediaId) = command;
        mediaId ??= Constants.User.Sherlock.MediaId;
        var chatChange = Change.Create<ChatDiff> (new() {
            IsPublic = false,
            Title = title,
            Kind = ChatKind.Group,
            MediaId = mediaId,
            SystemTag = Constants.Chat.SystemTags.Bot,
        });
        var chatChangeCommand = new Chats_Change(session, null, null, chatChange);
        var chat = await Commander.Call(
            chatChangeCommand,
            isOutermost: true,
            cancellationToken: cancellationToken
        ).ConfigureAwait(false);
        return new MLSearchChat(chat.Id);
    }
}
