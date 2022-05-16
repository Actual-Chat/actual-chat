using System.Security;
using ActualChat.Chat.Db;
using ActualChat.Users;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Chat;

public class Chats : DbServiceBase<ChatDbContext>, IChats
{
    private static readonly TileStack<long> IdTileStack = Constants.Chat.IdTileStack;

    private readonly ICommander _commander;
    private readonly IAuth _auth;
    private readonly IAuthBackend _authBackend;
    private readonly IChatAuthors _chatAuthors;
    private readonly IChatAuthorsBackend _chatAuthorsBackend;
    private readonly IUserContactsBackend _userContactsBackend;
    private readonly IChatsBackend _backend;

    public Chats(IServiceProvider services) : base(services)
    {
        _commander = Services.Commander();
        _auth = Services.GetRequiredService<IAuth>();
        _authBackend = Services.GetRequiredService<IAuthBackend>();
        _chatAuthors = Services.GetRequiredService<IChatAuthors>();
        _chatAuthorsBackend = Services.GetRequiredService<IChatAuthorsBackend>();
        _userContactsBackend = Services.GetRequiredService<IUserContactsBackend>();
        _backend = Services.GetRequiredService<IChatsBackend>();
    }

    // [ComputeMethod]
    public virtual async Task<Chat?> Get(Session session, string chatId, CancellationToken cancellationToken)
    {
        var isPeerChat = PeerChatExt.IsPeerChatId(chatId);
        return isPeerChat
            ? await GetPeerChat(session, chatId, cancellationToken).ConfigureAwait(false)
            : await GetGroupChat(session, chatId, cancellationToken).ConfigureAwait(false);
    }

    [ComputeMethod]
    protected virtual async Task<string> GetUsersPeerChatTitle(string chatId, User user, CancellationToken cancellationToken)
    {
        if (PeerChatExt.TryParseFullPeerChatId(chatId, out var userId1, out var userId2)) {
            var targetUserId = "";
            if (userId1 == user.Id)
                targetUserId = userId2;
            else if (userId2 == user.Id)
                targetUserId = userId1;
            if (!targetUserId.IsNullOrEmpty()) {
                var userContact = await _userContactsBackend.GetByTargetId(user.Id, targetUserId, cancellationToken).ConfigureAwait(false);
                if (userContact != null)
                    return userContact.Name;
                return await _userContactsBackend.SuggestContactName(targetUserId, cancellationToken).ConfigureAwait(false);
            }
        }
        return "p2p";
    }

    [ComputeMethod]
    protected virtual async Task<string> GetFullPeerChatId(Session session, string chatId, CancellationToken cancellationToken)
    {
        switch (PeerChatExt.GetChatIdKind(chatId)) {
            case PeerChatIdKind.None:
                return "";
            case PeerChatIdKind.Full:
                return chatId;
        }
        if (!PeerChatExt.TryParseShortPeerChatId(chatId, out var userId2))
            return "";
        var user = await _auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        if (!user.IsAuthenticated)
            return "";
        return PeerChatExt.CreateFullPeerChatId(user.Id, userId2);
    }

    // [ComputeMethod]
    public virtual async Task<Chat[]> GetChats(Session session, CancellationToken cancellationToken)
    {
        var chatIds = await _chatAuthors.GetChatIds(session, cancellationToken).ConfigureAwait(false);
        var user = await _auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        if (user.IsAuthenticated) {
            var ownedChatIds = await _backend.GetOwnedChatIds(user.Id, cancellationToken).ConfigureAwait(false);
            chatIds = chatIds.Union(ownedChatIds, StringComparer.Ordinal).ToArray();
        }

        var chatTasks = await Task
            .WhenAll(chatIds.Select(id => Get(session, id, cancellationToken)))
            .ConfigureAwait(false);
        return chatTasks.Where(c => c is { ChatType: ChatType.Group }).Select(c => c!).ToArray();
    }

    // [ComputeMethod]
    public virtual async Task<ChatTile> GetTile(
        Session session,
        string chatId,
        ChatEntryType entryType,
        Range<long> idTileRange,
        CancellationToken cancellationToken)
    {
        await AssertHasPermissions(session, chatId, ChatPermissions.Read, cancellationToken).ConfigureAwait(false);
        return await _backend.GetTile(chatId, entryType, idTileRange, false, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<long> GetEntryCount(
        Session session,
        string chatId,
        ChatEntryType entryType,
        Range<long>? idTileRange,
        CancellationToken cancellationToken)
    {
        await AssertHasPermissions(session, chatId, ChatPermissions.Read, cancellationToken).ConfigureAwait(false);
        return await _backend.GetEntryCount(chatId, entryType, idTileRange, false, cancellationToken).ConfigureAwait(false);
    }

    // Note that it returns (firstId, lastId + 1) range!
    // [ComputeMethod]
    public virtual async Task<Range<long>> GetIdRange(
        Session session,
        string chatId,
        ChatEntryType entryType,
        CancellationToken cancellationToken)
    {
        await AssertHasPermissions(session, chatId, ChatPermissions.Read, cancellationToken).ConfigureAwait(false);
        return await _backend.GetIdRange(chatId, entryType, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ChatPermissions> GetPermissions(
        Session session,
        string chatId,
        CancellationToken cancellationToken)
        => await _backend.GetPermissions(session, chatId, cancellationToken).ConfigureAwait(false);

    // [ComputeMethod]
    public virtual async Task<bool> CheckCanJoin(Session session, string chatId, CancellationToken cancellationToken)
    {
        var chat = await Get(session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat != null && chat.IsPublic)
            return true;

        var invited = await IsInvited(session, chatId, cancellationToken).ConfigureAwait(false);
        return invited;
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<TextEntryAttachment>> GetTextEntryAttachments(
        Session session, string chatId, long entryId, CancellationToken cancellationToken)
    {
        await AssertHasPermissions(session, chatId, ChatPermissions.Read, cancellationToken).ConfigureAwait(false);
        return await _backend.GetTextEntryAttachments(chatId, entryId, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<bool> CanSendPeerChatMessage(Session session, string chatPrincipalId, CancellationToken cancellationToken)
    {
        var user = await _auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        if (!user.IsAuthenticated)
            return false;

        if (ChatAuthor.TryGetChatId(chatPrincipalId, out var chatId)) {
            if (!await CheckHasPermissions(session, chatId, ChatPermissions.Read, cancellationToken)
                    .ConfigureAwait(false))
                return false;

            var chatAuthor = await _chatAuthorsBackend.Get(chatId, chatPrincipalId, false, cancellationToken)
                .ConfigureAwait(false);
            if (chatAuthor == null || chatAuthor.UserId.IsEmpty || chatAuthor.UserId == user.Id)
                return false;
        }
        else {
            var user2 = await _authBackend.GetUser(chatPrincipalId, cancellationToken).ConfigureAwait(false);
            if (user2 == null || !user2.IsAuthenticated || user2.Id == user.Id)
                return false;
        }
        return true;
    }

    public virtual async Task<string?> GetPeerChatId(Session session, string chatPrincipalId, CancellationToken cancellationToken)
    {
        if (!await CanSendPeerChatMessage(session, chatPrincipalId, cancellationToken).ConfigureAwait(false))
            return Symbol.Empty;
        var userId2 = await _chatAuthorsBackend.GetUserIdFromPrincipalId(chatPrincipalId, cancellationToken).ConfigureAwait(false);
        if (userId2 == null)
            return null;
        var peerChatId = PeerChatExt.CreateShortPeerChatId(userId2);
        return peerChatId;
    }

    public virtual async Task<MentionCandidate[]> GetMentionCandidates(Session session, string chatId, CancellationToken cancellationToken)
    {
        await AssertHasPermissions(session, chatId, ChatPermissions.Read, cancellationToken).ConfigureAwait(false);
        var chatAuthorIds = await _chatAuthorsBackend.GetAuthorIds(chatId, cancellationToken).ConfigureAwait(false);

        var authorTasks = await Task
            .WhenAll(chatAuthorIds.Select(id
                => _chatAuthors.GetAuthor(chatId, id, true, cancellationToken)))
            .ConfigureAwait(false);
        var items = authorTasks
            .Where(c => c != null)
            .Select(c => c!)
            .OrderBy(c => c.Name)
            .Select(c => new MentionCandidate("a:" + c.Id, c.Name))
            .ToArray();
        return items;
    }

    // [CommandHandler]
    public virtual async Task<Chat> CreateChat(IChats.CreateChatCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return default!; // It just spawns other commands, so nothing to do here

        var (session, title) = command;
        var user = await _auth.RequireUser(session, cancellationToken).ConfigureAwait(false);
        var chat = new Chat() {
            Title = title,
            IsPublic = command.IsPublic,
            OwnerIds = ImmutableArray.Create(user.Id),
        };
        var createChatCommand = new IChatsBackend.CreateChatCommand(chat);
        chat = await _commander.Call(createChatCommand, true, cancellationToken).ConfigureAwait(false);

        var createAuthorCommand = new IChatAuthorsBackend.CreateCommand(chat.Id, user.Id);
        _ = await _commander.Call(createAuthorCommand, cancellationToken).ConfigureAwait(false);
        return chat;
    }

    // [CommandHandler]
    public virtual async Task<Unit> UpdateChat(IChats.UpdateChatCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return default!; // It just spawns other commands, so nothing to do here

        var (session, chat) = command;
        await AssertHasPermissions(session, chat.Id, ChatPermissions.Admin, cancellationToken).ConfigureAwait(false);

        var updateChatCommand = new IChatsBackend.UpdateChatCommand(chat);
        return await _commander.Call(updateChatCommand, true, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<Unit> JoinChat(IChats.JoinChatCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return default!; // It just spawns other commands, so nothing to do here

        var (session, chatId) = command;

        var enoughPermissions = await CheckCanJoin(session, chatId, cancellationToken).ConfigureAwait(false);
        if (!enoughPermissions)
            PermissionsExt.Throw();

        await JoinChat(session, chatId, cancellationToken).ConfigureAwait(false);
        return Unit.Default;
    }

    // [CommandHandler]
    public virtual async Task<ChatEntry> CreateTextEntry(
        IChats.CreateTextEntryCommand command,
        CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return default!; // It just spawns other commands, so nothing to do here

        var (session, chatId, text) = command;
        // NOTE(AY): Temp. commented this out, coz it confuses lots of people who're trying to post in anonymous mode
        // await AssertHasPermissions(session, chatId, ChatPermissions.Write, cancellationToken).ConfigureAwait(false);
        var author = await _chatAuthorsBackend.GetOrCreate(session, chatId, cancellationToken).ConfigureAwait(false);

        var chatEntry = new ChatEntry() {
            ChatId = chatId,
            AuthorId = author.Id,
            Content = text,
            Type = ChatEntryType.Text,
            HasAttachments = command.Attachments.Length > 0
        };
        var upsertCommand = new IChatsBackend.UpsertEntryCommand(chatEntry);
        var textEntry =  await _commander.Call(upsertCommand, true, cancellationToken).ConfigureAwait(false);

        for (var i = 0; i < command.Attachments.Length; i++) {
            var attachmentUpload = command.Attachments[i];
            var (fileName, content, contentType) = attachmentUpload;
            var contentLocalId = Ulid.NewUlid().ToString();
            var contentId = $"attachments/{chatId}/{contentLocalId}/{fileName}";

            var saveCommand = new IContentSaverBackend.SaveContentCommand(contentId, content, contentType);
            await _commander.Call(saveCommand, true, cancellationToken).ConfigureAwait(false);

            var attachment = new TextEntryAttachment {
                Index = i,
                Length = content.Length,
                ChatId = chatId,
                EntryId = textEntry.Id,
                ContentType = contentType,
                FileName = fileName,
                ContentId = contentId
            };
            var createAttachmentCommand = new IChatsBackend.CreateTextEntryAttachmentCommand(attachment);
            await _commander.Call(createAttachmentCommand, true, cancellationToken).ConfigureAwait(false);
        }
        return textEntry;
    }

    // [CommandHandler]
    public virtual async Task RemoveTextEntry(IChats.RemoveTextEntryCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var (session, chatId, entryId) = command;
        await AssertHasPermissions(session, chatId, ChatPermissions.Write, cancellationToken).ConfigureAwait(false);
        var author = await _chatAuthorsBackend.GetOrCreate(session, chatId, cancellationToken).ConfigureAwait(false);

        var idTile = IdTileStack.FirstLayer.GetTile(entryId);
        var tile = await GetTile(session, chatId, ChatEntryType.Text, idTile.Range, cancellationToken).ConfigureAwait(false);
        var chatEntry = tile.Entries.Single(e => e.Id == entryId);
        if (chatEntry.AuthorId != author.Id)
            throw new SecurityException("You can delete only your own messages.");

        chatEntry = chatEntry with { IsRemoved = true };
        var upsertCommand = new IChatsBackend.UpsertEntryCommand(chatEntry);
        await _commander.Call(upsertCommand, true, cancellationToken).ConfigureAwait(false);
    }

    // Protected methods

    private async Task JoinChat(Session session, string chatId, CancellationToken cancellationToken)
        => await _chatAuthorsBackend.GetOrCreate(session, chatId, cancellationToken).ConfigureAwait(false);

    private async Task AssertHasPermissions(Session session, string chatId,
        ChatPermissions permissions, CancellationToken cancellationToken)
    {
        var enoughPermissions = await CheckHasPermissions(session, chatId, permissions, cancellationToken).ConfigureAwait(false);
        if (!enoughPermissions)
            PermissionsExt.Throw();
    }

    private async Task<bool> CheckHasPermissions(Session session, string chatId,
        ChatPermissions requiredPermissions, CancellationToken cancellationToken)
    {
        var permissions = await _backend.GetPermissions(session, chatId, cancellationToken).ConfigureAwait(false);
        var enoughPermissions = permissions.CheckHasPermissions(requiredPermissions);
        if (!enoughPermissions && requiredPermissions==ChatPermissions.Read)
            return await IsInvited(session, chatId, cancellationToken).ConfigureAwait(false);
        return enoughPermissions;
    }

    private async Task<Chat?> GetGroupChat(Session session, string chatId, CancellationToken cancellationToken)
    {
        var canRead = await CheckHasPermissions(session, chatId, ChatPermissions.Read, cancellationToken)
            .ConfigureAwait(false);
        if (!canRead)
            return null;
        return await _backend.Get(chatId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> IsInvited(
        Session session,
        string chatId,
        CancellationToken cancellationToken)
    {
        var options = await _auth.GetOptions(session, cancellationToken).ConfigureAwait(false);
        if (!options.Items.TryGetValue("Invite::ChatId", out var inviteChatId))
            return false;
        return StringComparer.Ordinal.Equals(chatId, inviteChatId as string);
    }

    [ComputeMethod]
    protected virtual async Task<Chat?> GetPeerChat(Session session, string chatId, CancellationToken cancellationToken)
    {
        switch (PeerChatExt.GetChatIdKind(chatId)) {
        case PeerChatIdKind.Short:
            chatId = await GetFullPeerChatId(session, chatId, cancellationToken).ConfigureAwait(false);
            return await GetPeerChat(session, chatId, cancellationToken).ConfigureAwait(false);
        case PeerChatIdKind.Full:
            break;
        default:
            return null;
        }

        var canRead = await CheckHasPermissions(session, chatId, ChatPermissions.Read, cancellationToken).ConfigureAwait(false);
        if (!canRead)
            return null;

        var chat = await _backend.Get(chatId, cancellationToken).ConfigureAwait(false);
        chat ??= new Chat {
            Id = chatId,
            ChatType = ChatType.PeerToPeer,
        };
        var user = await _auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        var newTitle = await GetUsersPeerChatTitle(chatId, user, cancellationToken).ConfigureAwait(false);
        chat = chat with { Title = newTitle };
        return chat;
    }
}
