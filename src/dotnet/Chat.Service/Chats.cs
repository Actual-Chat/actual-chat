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
    private readonly IChatAuthors _chatAuthors;
    private readonly IChatAuthorsBackend _chatAuthorsBackend;
    private readonly IChatsBackend _chatsBackend;
    private readonly IInviteCodesBackend _inviteCodesBackend;
    private readonly IUserContactsBackend _userContactsBackend;

    public Chats(IServiceProvider services) : base(services)
    {
        _commander = Services.Commander();
        _auth = Services.GetRequiredService<IAuth>();
        _chatAuthors = Services.GetRequiredService<IChatAuthors>();
        _chatAuthorsBackend = Services.GetRequiredService<IChatAuthorsBackend>();
        _chatsBackend = Services.GetRequiredService<IChatsBackend>();
        _inviteCodesBackend = Services.GetRequiredService<IInviteCodesBackend>();
        _userContactsBackend = Services.GetRequiredService<IUserContactsBackend>();
    }

    // [ComputeMethod]
    public virtual async Task<Chat?> Get(Session session, string chatId, CancellationToken cancellationToken)
    {
        var canRead = await CheckHasPermissions(session, chatId, ChatPermissions.Read, cancellationToken).ConfigureAwait(false);
        if (!canRead)
            return null;
        return await _chatsBackend.Get(chatId, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<Symbol> GetAuthorsPeerChatId(Session session, string chatAuthorId, CancellationToken cancellationToken)
    {
        if (!ChatAuthor.TryParse(chatAuthorId, out var chatId, out var localId1))
            return Symbol.Empty;
        var chatAuthor = await _chatAuthorsBackend.Get(chatId, chatAuthorId, false, cancellationToken).ConfigureAwait(false);
        if (chatAuthor == null)
            return Symbol.Empty;
        var chatAuthor2 = await _chatAuthorsBackend.GetOrCreate(session, chatId, cancellationToken).ConfigureAwait(false);
        if (!ChatAuthor.TryParse(chatAuthor2.Id, out _, out var localId2))
            return Symbol.Empty;
        var peerChatId = PeerChatExt.CreateAuthorsPeerChatId(chatId, localId1, localId2);
        return peerChatId;
    }

    public virtual async Task<Chat?> GetDirectChat(Session session, string chatIdentifier, CancellationToken cancellationToken)
    {
        var isAuthorsChatIdentifier = PeerChatExt.IsAuthorsPeerChatId(chatIdentifier);
        if (isAuthorsChatIdentifier)
            return await GetDirectAuthorsChatById(session, chatIdentifier, cancellationToken).ConfigureAwait(false);
        return await GetDirectChatByContact(session, chatIdentifier, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<Chat[]> GetChats(Session session, CancellationToken cancellationToken)
    {
        var chatIds = await _chatAuthors.GetChatIds(session, cancellationToken).ConfigureAwait(false);
        var user = await _auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        if (user.IsAuthenticated) {
            var ownedChatIds = await _chatsBackend.GetOwnedChatIds(user.Id, cancellationToken).ConfigureAwait(false);
            chatIds = chatIds.Union(ownedChatIds, StringComparer.Ordinal).ToArray();
        }

        var chatTasks = await Task
            .WhenAll(chatIds.Select(id => Get(session, id, cancellationToken)))
            .ConfigureAwait(false);
        return chatTasks.Where(c => c != null && c.ChatType == ChatType.Group).Select(c => c!).ToArray();
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
        return await _chatsBackend.GetTile(chatId, entryType, idTileRange, false, cancellationToken).ConfigureAwait(false);
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
        return await _chatsBackend.GetEntryCount(chatId, entryType, idTileRange, false, cancellationToken).ConfigureAwait(false);
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
        return await _chatsBackend.GetIdRange(chatId, entryType, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ChatPermissions> GetPermissions(
        Session session,
        string chatId,
        CancellationToken cancellationToken)
        => await _chatsBackend.GetPermissions(session, chatId, cancellationToken).ConfigureAwait(false);

    // [ComputeMethod]
    public virtual async Task<bool> CheckCanJoin(Session session, string chatId, CancellationToken cancellationToken)
    {
        var chat = await Get(session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat != null && chat.IsPublic)
            return true;
        var invited = await _inviteCodesBackend.CheckIfInviteCodeUsed(session, chatId, cancellationToken).ConfigureAwait(false);
        return invited;
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<TextEntryAttachment>> GetTextEntryAttachments(
        Session session, string chatId, long entryId, CancellationToken cancellationToken)
    {
        await AssertHasPermissions(session, chatId, ChatPermissions.Read, cancellationToken).ConfigureAwait(false);
        return await _chatsBackend.GetTextEntryAttachments(chatId, entryId, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<Chat> CreateChat(IChats.CreateChatCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return default!; // It just spawns other commands, so nothing to do here

        var (session, title) = command;
        var user = await _auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        user.MustBeAuthenticated();

        var chat = new Chat() {
            Title = title,
            IsPublic = command.IsPublic,
            OwnerIds = ImmutableArray.Create(user.Id),
        };
        var createChatCommand = new IChatsBackend.CreateChatCommand(chat);
        return await _commander.Call(createChatCommand, true, cancellationToken).ConfigureAwait(false);
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
        var permissions = await _chatsBackend.GetPermissions(session, chatId, cancellationToken).ConfigureAwait(false);
        var enoughPermissions = permissions.CheckHasPermissions(requiredPermissions);
        if (!enoughPermissions && requiredPermissions==ChatPermissions.Read)
            return await _inviteCodesBackend.CheckIfInviteCodeUsed(session, chatId, cancellationToken).ConfigureAwait(false);
        return enoughPermissions;
    }


    private async Task<Chat?> GetDirectChatByContact(Session session, string userContactId, CancellationToken cancellationToken)
    {
        var userContact = await _userContactsBackend.Get(userContactId, cancellationToken).ConfigureAwait(false);
        if (userContact == null)
            return null;
        // if (userContact == null) {
        //     if (ChatAuthor.TryGetChatId(userContactId, out var chatId)) {
        //         var chatAuthor = await _chatAuthorsBackend.Get(chatId, userContactId, true, default)
        //             .ConfigureAwait(false);
        //         if (chatAuthor != null) {
        //             var directChatId1 =  chatAuthor.Id.CreateDirectAuthorChatId();
        //             return new Chat {
        //                 Id = directChatId1,
        //                 Title = chatAuthor.Name,
        //             };
        //         }
        //     }
        // }

        var user = await _auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        if (!user.IsAuthenticated)
            return null;
        if (userContact.OwnerUserId != user.Id)
            return null;

        var firstUserId = user.Id;
        var secondUserId = userContact.TargetUserId;
        if (string.Compare(firstUserId, secondUserId, StringComparison.Ordinal) < 0)
            (firstUserId, secondUserId) = (secondUserId, firstUserId);

        var directChatId = "direct:" + firstUserId + ":" + secondUserId;
        var directChat = await _chatsBackend.Get(directChatId, cancellationToken).ConfigureAwait(false);
        if (directChat == null) {
            var pmChatInfo = new Chat {
                Id = directChatId,
                OwnerIds = ImmutableArray<Symbol>.Empty.Add(firstUserId).Add(secondUserId),
                Title = "Direct chat",
                ChatType = ChatType.Direct
            };
            var createChatCommand = new IChatsBackend.CreateChatCommand(pmChatInfo);
            directChat = await _chatsBackend.CreateChat(createChatCommand, cancellationToken).ConfigureAwait(false);
        }

        directChat = directChat with {Title = userContact.Name};
        return directChat;
    }

    private async Task<Chat?> GetDirectAuthorsChatById(Session session, string chatId, CancellationToken cancellationToken)
    {
        var canRead = await CheckHasPermissions(session, chatId, ChatPermissions.Read, cancellationToken).ConfigureAwait(false);
        if (!canRead)
            return null;
        var chat = await _chatsBackend.Get(chatId, cancellationToken).ConfigureAwait(false);
        chat ??= new Chat {
            Id = chatId,
            ChatType = ChatType.Direct
        };
        var user = await _auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        var newTitle = await GetAuthorsPeerChatTitle(chatId, user, cancellationToken).ConfigureAwait(false);
        chat = chat with { Title = newTitle };
        return chat;
    }

    private async Task<string> GetAuthorsPeerChatTitle(Symbol chatId, User user, CancellationToken cancellationToken)
    {
        PeerChatExt.TryParseAuthorsPeerChatId(chatId, out var originalChatId, out var localId1, out var localId2);
        var chatAuthor1 = await _chatAuthorsBackend
            .Get(originalChatId, DbChatAuthor.ComposeId(originalChatId, localId1), false, cancellationToken)
            .ConfigureAwait(false);
        var chatAuthor2 = await _chatAuthorsBackend
            .Get(originalChatId, DbChatAuthor.ComposeId(originalChatId, localId2), false, cancellationToken)
            .ConfigureAwait(false);
        var targetUserId = Symbol.Empty;
        if (chatAuthor1 != null && chatAuthor2 != null) {
            if (chatAuthor1.UserId == user.Id)
                targetUserId = chatAuthor2.UserId;
            else if (chatAuthor2.UserId == user.Id)
                targetUserId = chatAuthor1.UserId;
        }
        return await GetAuthorsPeerChatTitle(originalChatId, targetUserId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> GetAuthorsPeerChatTitle(Symbol originalChatId, Symbol userId, CancellationToken cancellationToken)
    {
        var originalChat = await _chatsBackend.Get(originalChatId, cancellationToken).ConfigureAwait(false);
        ChatAuthor? chatAuthor = null;
        if (!userId.IsEmpty)
            chatAuthor = await _chatAuthorsBackend.GetByUserId(originalChatId, userId, true, cancellationToken)
                .ConfigureAwait(false);
        var newTitle = "unknown";
        if (chatAuthor != null)
            newTitle = chatAuthor.Name;
        if (originalChat != null)
            newTitle += " (" + originalChat.Title + ")";
        return newTitle;
    }
}
