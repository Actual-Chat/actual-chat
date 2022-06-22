using System.Security;
using ActualChat.Chat.Db;
using ActualChat.Users;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Chat;

public class Chats : DbServiceBase<ChatDbContext>, IChats
{
    private static readonly TileStack<long> IdTileStack = Constants.Chat.IdTileStack;

    private ICommander Commander { get; }
    private IAuth Auth { get; }
    private IAuthBackend AuthBackend { get; }
    private IChatAuthors ChatAuthors { get; }
    private IChatAuthorsBackend ChatAuthorsBackend { get; }
    private IUserContactsBackend UserContactsBackend { get; }
    private IChatsBackend Backend { get; }

    public Chats(IServiceProvider services) : base(services)
    {
        Commander = Services.Commander();
        Auth = Services.GetRequiredService<IAuth>();
        AuthBackend = Services.GetRequiredService<IAuthBackend>();
        ChatAuthors = Services.GetRequiredService<IChatAuthors>();
        ChatAuthorsBackend = Services.GetRequiredService<IChatAuthorsBackend>();
        UserContactsBackend = Services.GetRequiredService<IUserContactsBackend>();
        Backend = Services.GetRequiredService<IChatsBackend>();
    }

    // [ComputeMethod]
    public virtual async Task<Chat?> Get(Session session, string chatId, CancellationToken cancellationToken)
    {
        var isPeerChat = ChatId.IsPeerChatId(chatId);
        return isPeerChat
            ? await GetPeerChat(session, chatId, cancellationToken).ConfigureAwait(false)
            : await GetGroupChat(session, chatId, cancellationToken).ConfigureAwait(false);
    }

    [ComputeMethod]
    protected virtual async Task<string> GetPeerChatTitle(string chatId, User user, CancellationToken cancellationToken)
    {
        if (ChatId.TryParseFullPeerChatId(chatId, out var userId1, out var userId2)) {
            var targetUserId = "";
            if (userId1 == user.Id)
                targetUserId = userId2;
            else if (userId2 == user.Id)
                targetUserId = userId1;
            if (!targetUserId.IsNullOrEmpty()) {
                var userContact = await UserContactsBackend.GetByTargetId(user.Id, targetUserId, cancellationToken).ConfigureAwait(false);
                if (userContact != null)
                    return userContact.Name;
                return await UserContactsBackend.SuggestContactName(targetUserId, cancellationToken).ConfigureAwait(false);
            }
        }
        return "p2p";
    }

    [ComputeMethod]
    protected virtual async Task<string> GetFullPeerChatId(Session session, string chatId, CancellationToken cancellationToken)
    {
        switch (ChatId.GetChatIdType(chatId)) {
        case ChatIdType.PeerFull:
            return chatId;
        case ChatIdType.PeerShort:
            if (!ChatId.TryParseShortPeerChatId(chatId, out var userId2))
                throw new ArgumentOutOfRangeException(nameof(chatId));
            var user = await Auth.GetUser(session, cancellationToken).ConfigureAwait(false);
            user.MustBeAuthenticated();
            return ChatId.FormatFullPeerChatId(user.Id, userId2);
        default:
            throw new ArgumentOutOfRangeException(nameof(chatId));
        }
    }

    // [ComputeMethod]
    public virtual async Task<Chat[]> GetChats(Session session, CancellationToken cancellationToken)
    {
        var chatIds = await ChatAuthors.ListOwnChatIds(session, cancellationToken).ConfigureAwait(false);
        var user = await Auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        if (user.IsAuthenticated) {
            var ownedChatIds = await Backend.ListOwnedChatIds(user.Id, cancellationToken).ConfigureAwait(false);
            chatIds = chatIds.Union(ownedChatIds).ToImmutableArray();
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
        await DemandPermissions(session, chatId, ChatPermissions.Read, cancellationToken).ConfigureAwait(false);
        return await Backend.GetTile(chatId, entryType, idTileRange, false, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<long> GetEntryCount(
        Session session,
        string chatId,
        ChatEntryType entryType,
        Range<long>? idTileRange,
        CancellationToken cancellationToken)
    {
        await DemandPermissions(session, chatId, ChatPermissions.Read, cancellationToken).ConfigureAwait(false);
        return await Backend.GetEntryCount(chatId, entryType, idTileRange, false, cancellationToken).ConfigureAwait(false);
    }

    // Note that it returns (firstId, lastId + 1) range!
    // [ComputeMethod]
    public virtual async Task<Range<long>> GetIdRange(
        Session session,
        string chatId,
        ChatEntryType entryType,
        CancellationToken cancellationToken)
    {
        await DemandPermissions(session, chatId, ChatPermissions.Read, cancellationToken).ConfigureAwait(false);
        return await Backend.GetIdRange(chatId, entryType, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<Range<long>> GetLastIdTile0(
        Session session,
        string chatId,
        ChatEntryType entryType,
        CancellationToken cancellationToken)
    {
        await DemandPermissions(session, chatId, ChatPermissions.Read, cancellationToken).ConfigureAwait(false);
        return await Backend.GetLastIdTile0(chatId, entryType, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<Range<long>> GetLastIdTile1(
        Session session,
        string chatId,
        ChatEntryType entryType,
        CancellationToken cancellationToken)
    {
        await DemandPermissions(session, chatId, ChatPermissions.Read, cancellationToken).ConfigureAwait(false);
        return await Backend.GetLastIdTile0(chatId, entryType, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ChatAuthorRules> GetRules(
        Session session,
        string chatId,
        CancellationToken cancellationToken)
        => await Backend.GetRules(session, chatId, cancellationToken).ConfigureAwait(false);

    // [ComputeMethod]
    public virtual async Task<bool> CanJoin(Session session, string chatId, CancellationToken cancellationToken)
    {
        var chat = await Get(session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat is { IsPublic: true })
            return true;

        var invited = await IsInvited(session, chatId, cancellationToken).ConfigureAwait(false);
        return invited;
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<TextEntryAttachment>> GetTextEntryAttachments(
        Session session, string chatId, long entryId, CancellationToken cancellationToken)
    {
        await DemandPermissions(session, chatId, ChatPermissions.Read, cancellationToken).ConfigureAwait(false);
        return await Backend.GetTextEntryAttachments(chatId, entryId, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<bool> CanSendPeerChatMessage(Session session, string chatPrincipalId, CancellationToken cancellationToken)
    {
        var user = await Auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        if (!user.IsAuthenticated)
            return false;

        if (ChatAuthor.TryParseId(chatPrincipalId, out var chatId, out _)) {
            if (!await HasPermissions(session, chatId, ChatPermissions.Read, cancellationToken)
                    .ConfigureAwait(false))
                return false;

            var chatAuthor = await ChatAuthorsBackend.Get(chatId, chatPrincipalId, false, cancellationToken)
                .ConfigureAwait(false);
            if (chatAuthor == null || chatAuthor.UserId.IsEmpty || chatAuthor.UserId == user.Id)
                return false;
        }
        else {
            var user2 = await AuthBackend.GetUser(chatPrincipalId, cancellationToken).ConfigureAwait(false);
            if (user2 == null || !user2.IsAuthenticated || user2.Id == user.Id)
                return false;
        }
        return true;
    }

    public virtual async Task<string?> GetPeerChatId(Session session, string chatPrincipalId, CancellationToken cancellationToken)
    {
        if (!await CanSendPeerChatMessage(session, chatPrincipalId, cancellationToken).ConfigureAwait(false))
            return Symbol.Empty;
        var userId2 = await ChatAuthorsBackend.GetUserIdFromPrincipalId(chatPrincipalId, cancellationToken).ConfigureAwait(false);
        if (userId2 == null)
            return null;
        var peerChatId = ChatId.FormatShortPeerChatId(userId2);
        return peerChatId;
    }

    public virtual async Task<MentionCandidate[]> GetMentionCandidates(Session session, string chatId, CancellationToken cancellationToken)
    {
        await DemandPermissions(session, chatId, ChatPermissions.Read, cancellationToken).ConfigureAwait(false);
        var chatAuthorIds = await ChatAuthorsBackend.ListAuthorIds(chatId, cancellationToken).ConfigureAwait(false);

        var authorTasks = await Task
            .WhenAll(chatAuthorIds.Select(id
                => ChatAuthors.GetAuthor(chatId, id, true, cancellationToken)))
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
        var user = await Auth.RequireUser(session, cancellationToken).ConfigureAwait(false);
        var chat = new Chat() {
            Title = title,
            IsPublic = command.IsPublic,
            OwnerIds = ImmutableArray.Create(user.Id),
        };
        var createChatCommand = new IChatsBackend.CreateChatCommand(chat);
        chat = await Commander.Call(createChatCommand, true, cancellationToken).ConfigureAwait(false);

        var createAuthorCommand = new IChatAuthorsBackend.CreateCommand(chat.Id, user.Id);
        _ = await Commander.Call(createAuthorCommand, cancellationToken).ConfigureAwait(false);
        return chat;
    }

    // [CommandHandler]
    public virtual async Task<Unit> UpdateChat(IChats.UpdateChatCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return default!; // It just spawns other commands, so nothing to do here

        var (session, chat) = command;
        await DemandPermissions(session, chat.Id, ChatPermissions.EditProperties, cancellationToken).ConfigureAwait(false);

        var updateChatCommand = new IChatsBackend.UpdateChatCommand(chat);
        return await Commander.Call(updateChatCommand, true, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<Unit> JoinChat(IChats.JoinChatCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return default!; // It just spawns other commands, so nothing to do here

        var (session, chatId) = command;

        if (!await CanJoin(session, chatId, cancellationToken).ConfigureAwait(false))
            throw ChatPermissionsExt.NotEnoughPermissions();

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
        var author = await ChatAuthorsBackend.GetOrCreate(session, chatId, cancellationToken).ConfigureAwait(false);

        var chatEntry = new ChatEntry {
            ChatId = chatId,
            AuthorId = author.Id,
            Content = text,
            Type = ChatEntryType.Text,
            HasAttachments = command.Attachments.Length > 0,
            RepliedChatEntryId = command.RepliedChatEntryId!,
        };
        var upsertCommand = new IChatsBackend.UpsertEntryCommand(chatEntry);
        var textEntry =  await Commander.Call(upsertCommand, true, cancellationToken).ConfigureAwait(false);

        for (var i = 0; i < command.Attachments.Length; i++) {
            var attachmentUpload = command.Attachments[i];
            var (fileName, content, contentType) = attachmentUpload;
            var contentLocalId = Ulid.NewUlid().ToString();
            var contentId = $"attachments/{chatId}/{contentLocalId}/{fileName}";

            var saveCommand = new IContentSaverBackend.SaveContentCommand(contentId, content, contentType);
            await Commander.Call(saveCommand, true, cancellationToken).ConfigureAwait(false);

            var attachment = new TextEntryAttachment {
                Index = i,
                Length = content.Length,
                ChatId = chatId,
                EntryId = textEntry.Id,
                ContentType = contentType,
                FileName = fileName,
                ContentId = contentId,
                Width = attachmentUpload.Width,
                Height = attachmentUpload.Height,
            };
            var createAttachmentCommand = new IChatsBackend.CreateTextEntryAttachmentCommand(attachment);
            await Commander.Call(createAttachmentCommand, true, cancellationToken).ConfigureAwait(false);
        }
        return textEntry;
    }

    // [CommandHandler]
    public virtual async Task RemoveTextEntry(IChats.RemoveTextEntryCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var (session, chatId, entryId) = command;
        await DemandPermissions(session, chatId, ChatPermissions.Write, cancellationToken).ConfigureAwait(false);
        var textEntry = await RemoveChatEntry(session, chatId, entryId, ChatEntryType.Text, cancellationToken).ConfigureAwait(false);

        if (textEntry.AudioEntryId != null)
            await RemoveChatEntry(session, chatId, textEntry.AudioEntryId.Value, ChatEntryType.Audio, cancellationToken).ConfigureAwait(false);
    }

    // Protected methods

    [ComputeMethod]
    protected virtual async Task<Chat?> GetPeerChat(Session session, string chatId, CancellationToken cancellationToken)
    {
        switch (ChatId.GetChatIdType(chatId)) {
        case ChatIdType.PeerShort:
            chatId = await GetFullPeerChatId(session, chatId, cancellationToken).ConfigureAwait(false);
            return await GetPeerChat(session, chatId, cancellationToken).ConfigureAwait(false);
        case ChatIdType.PeerFull:
            break;
        default:
            return null;
        }

        var canRead = await HasPermissions(session, chatId, ChatPermissions.Read, cancellationToken).ConfigureAwait(false);
        if (!canRead)
            return null;

        var chat = await Backend.Get(chatId, cancellationToken).ConfigureAwait(false);
        chat ??= new Chat {
            Id = chatId,
            ChatType = ChatType.Peer,
        };
        var user = await Auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        var title = await GetPeerChatTitle(chatId, user, cancellationToken).ConfigureAwait(false);
        chat = chat with { Title = title };
        return chat;
    }

    // Private methods

    private async Task<ChatEntry> RemoveChatEntry(Session session, string chatId, long entryId, ChatEntryType type, CancellationToken cancellationToken)
    {
        var chatEntry = await GetChatEntry(session, chatId, entryId, type, cancellationToken).ConfigureAwait(false);

        var author = await ChatAuthorsBackend.GetOrCreate(session, chatId, cancellationToken).ConfigureAwait(false);
        if (chatEntry.AuthorId != author.Id)
            throw new SecurityException("You can delete only your own messages.");

        chatEntry = chatEntry with { IsRemoved = true };
        var upsertCommand = new IChatsBackend.UpsertEntryCommand(chatEntry);
        await Commander.Call(upsertCommand, true, cancellationToken).ConfigureAwait(false);
        return chatEntry;
    }

    private async Task<ChatEntry> GetChatEntry(Session session, string chatId, long entryId, ChatEntryType type, CancellationToken cancellationToken)
    {
        var idTile = IdTileStack.FirstLayer.GetTile(entryId);
        var tile = await GetTile(session, chatId, type, idTile.Range, cancellationToken)
            .ConfigureAwait(false);
        var chatEntry = tile.Entries.Single(e => e.Id == entryId);
        return chatEntry;
    }

    private async Task JoinChat(Session session, string chatId, CancellationToken cancellationToken)
        => await ChatAuthorsBackend.GetOrCreate(session, chatId, cancellationToken).ConfigureAwait(false);

    private async Task DemandPermissions(
        Session session, string chatId, ChatPermissions required,
        CancellationToken cancellationToken)
    {
        if (!await HasPermissions(session, chatId, required, cancellationToken).ConfigureAwait(false))
            throw ChatPermissionsExt.NotEnoughPermissions(required);
    }

    private async Task<bool> HasPermissions(
        Session session, string chatId, ChatPermissions required,
        CancellationToken cancellationToken)
    {
        var permissions = await Backend.GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        var hasPermissions = permissions.Has(required);
        if (!hasPermissions && required == ChatPermissions.Read) {
            // NOTE(AY): Maybe makes sense to move this to UI code - i.e. process the invite there
            // in case there is not enough permissions & retry.
            return await IsInvited(session, chatId, cancellationToken).ConfigureAwait(false);
        }
        return hasPermissions;
    }

    private async Task<Chat?> GetGroupChat(Session session, string chatId, CancellationToken cancellationToken)
    {
        var canRead = await HasPermissions(session, chatId, ChatPermissions.Read, cancellationToken)
            .ConfigureAwait(false);
        if (!canRead)
            return null;
        return await Backend.Get(chatId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> IsInvited(
        Session session,
        string chatId,
        CancellationToken cancellationToken)
    {
        var options = await Auth.GetOptions(session, cancellationToken).ConfigureAwait(false);
        if (!options.Items.TryGetValue("Invite::ChatId", out var inviteChatId))
            return false;
        return OrdinalEquals(chatId, inviteChatId as string);
    }
}
