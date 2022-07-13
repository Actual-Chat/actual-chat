using System.Security;
using ActualChat.Chat.Db;
using ActualChat.Users;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Chat;

public class Chats : DbServiceBase<ChatDbContext>, IChats
{
    private static readonly TileStack<long> IdTileStack = Constants.Chat.IdTileStack;
    private const string InvitedChatIdOptionKey = "Invite::ChatId"; // comes from InvitesBackend.Use
    private const string InvitedIdOptionKey = "Invite::Id"; // comes from InvitesBackend.Use

    private IAuth Auth { get; }
    private IAuthBackend AuthBackend { get; }
    private IUserProfiles UserProfiles { get; }
    private IChatAuthors ChatAuthors { get; }
    private IChatAuthorsBackend ChatAuthorsBackend { get; }
    private IUserContactsBackend UserContactsBackend { get; }
    private IChatsBackend Backend { get; }

    public Chats(IServiceProvider services) : base(services)
    {
        Auth = Services.GetRequiredService<IAuth>();
        AuthBackend = Services.GetRequiredService<IAuthBackend>();
        UserProfiles = Services.GetRequiredService<IUserProfiles>();
        ChatAuthors = Services.GetRequiredService<IChatAuthors>();
        ChatAuthorsBackend = Services.GetRequiredService<IChatAuthorsBackend>();
        UserContactsBackend = Services.GetRequiredService<IUserContactsBackend>();
        Backend = Services.GetRequiredService<IChatsBackend>();
    }

    // [ComputeMethod]
    public virtual async Task<Chat?> Get(Session session, string chatId, CancellationToken cancellationToken)
    {
        var isPeerChat = new ParsedChatId(chatId).Kind.IsPeer();
        return isPeerChat
            ? await GetPeerChat(session, chatId, cancellationToken).ConfigureAwait(false)
            : await GetGroupChat(session, chatId, cancellationToken).ConfigureAwait(false);
    }

    [ComputeMethod]
    protected virtual async Task<string> GetPeerChatTitle(string chatId, User user, CancellationToken cancellationToken)
    {
        var parsedChatId = new ParsedChatId(chatId).AssertPeerFull();
        var (userId1, userId2) = (parsedChatId.UserId1.Id, parsedChatId.UserId2.Id);

        var otherUserId = (userId1, userId2).OtherThan(user.Id);
        if (otherUserId.IsEmpty)
            throw new InvalidOperationException("Specified peer chat doesn't belong to the current user.");

        var contact = await UserContactsBackend.Get(user.Id, otherUserId, cancellationToken).ConfigureAwait(false);
        if (contact != null)
            return contact.Name;
        return await UserContactsBackend.SuggestContactName(otherUserId, cancellationToken).ConfigureAwait(false);
    }

    [ComputeMethod]
    protected virtual async Task<string> GetFullPeerChatId(Session session, string chatId, CancellationToken cancellationToken)
    {
        var parsedChatId = new ParsedChatId(chatId).AssertValid();
        switch (parsedChatId.Kind) {
        case ChatIdKind.PeerFull:
            return chatId;
        case ChatIdKind.PeerShort:
            var user = await Auth.GetUser(session, cancellationToken).Require().ConfigureAwait(false);
            return ParsedChatId.FormatFullPeerChatId(user.Id, parsedChatId.UserId1);
        default:
            throw new ArgumentOutOfRangeException(nameof(chatId));
        }
    }

    // [ComputeMethod]
    public virtual async Task<Chat[]> GetChats(Session session, CancellationToken cancellationToken)
    {
        var chatIds = await ChatAuthors.ListOwnChatIds(session, cancellationToken).ConfigureAwait(false);
        var user = await Auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        if (user != null) {
            var ownedChatIds = await Backend.ListOwnedChatIds(user.Id, cancellationToken).ConfigureAwait(false);
            chatIds = chatIds.Union(ownedChatIds).ToImmutableArray();

            var userProfile = await UserProfiles.Get(session, cancellationToken).ConfigureAwait(false) ?? throw new Exception("UserProfile not found for ");
            if (userProfile.IsAdmin && !chatIds.Contains(Constants.Chat.DefaultChatId))
                chatIds = chatIds.Add(Constants.Chat.DefaultChatId);
        }

        var chatTasks = await chatIds
            .Select(id => Get(session, id, cancellationToken))
            .Collect(cancellationToken)
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
    {
        var chatPrincipalId = await ChatAuthors.GetOwnPrincipalId(session, chatId, cancellationToken).ConfigureAwait(false);
        return await Backend.GetRules(chatId, chatPrincipalId, cancellationToken).ConfigureAwait(false);
    }

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
        if (user == null)
            return false;

        var parsedChatPrincipalId = new ParsedChatPrincipalId(chatPrincipalId);
        if (!parsedChatPrincipalId.IsValid)
            return false;

        if (parsedChatPrincipalId.Kind is ChatPrincipalKind.Author) {
            var chatId = parsedChatPrincipalId.AuthorId.ChatId.Id;
            if (!await HasPermissions(session, chatId, ChatPermissions.Read, cancellationToken)
                    .ConfigureAwait(false))
                return false;

            var chatAuthor = await ChatAuthorsBackend.Get(chatId, chatPrincipalId, false, cancellationToken)
                .ConfigureAwait(false);
            if (chatAuthor == null || chatAuthor.UserId.IsEmpty || chatAuthor.UserId == user.Id)
                return false;
        }
        else {
            var userId2 = parsedChatPrincipalId.UserId.Id;
            var user2 = await AuthBackend.GetUser(default, userId2, cancellationToken).ConfigureAwait(false);
            if (user2 == null || user2.Id == user.Id)
                return false;
        }
        return true;
    }

    public virtual async Task<string?> GetPeerChatId(Session session, string chatPrincipalId, CancellationToken cancellationToken)
    {
        if (!await CanSendPeerChatMessage(session, chatPrincipalId, cancellationToken).ConfigureAwait(false))
            return null;

        var userId2 = await ChatAuthorsBackend
            .GetUserId(chatPrincipalId, cancellationToken)
            .ConfigureAwait(false);
        if (userId2.IsEmpty)
            return null;

        return ParsedChatId.FormatShortPeerChatId(userId2);
    }

    public virtual async Task<MentionCandidate[]> GetMentionCandidates(Session session, string chatId, CancellationToken cancellationToken)
    {
        await DemandPermissions(session, chatId, ChatPermissions.Read, cancellationToken).ConfigureAwait(false);
        var chatAuthorIds = await ChatAuthorsBackend.ListAuthorIds(chatId, cancellationToken).ConfigureAwait(false);

        var authorTasks = await chatAuthorIds
            .Select(id => ChatAuthors.GetAuthor(chatId, id, true, cancellationToken))
            .Collect(cancellationToken)
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
        var userProfile = await UserProfiles.Get(session, cancellationToken)
            .Require(UserProfile.MustBeActive)
            .ConfigureAwait(false);
        var chat = new Chat() {
            Title = title,
            IsPublic = command.IsPublic,
            OwnerIds = ImmutableArray.Create(userProfile.Id),
        };
        var createChatCommand = new IChatsBackend.CreateChatCommand(chat);
        chat = await Commander.Call(createChatCommand, true, cancellationToken).ConfigureAwait(false);

        var createAuthorCommand = new IChatAuthorsBackend.CreateCommand(chat.Id, userProfile.Id);
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

    public virtual async Task LeaveChat(IChats.LeaveChatCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here
        var (session, chatId) = command;
        // NOTE(DF): may be replace with another membership check
        var chatAuthor = await ChatAuthors.GetOwnAuthor(session, chatId, cancellationToken).ConfigureAwait(false);
        if (chatAuthor == null || chatAuthor.HasLeft)
            return;
        var chat = await Get(session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return;

        var userProfile = await UserProfiles.Get(session, cancellationToken)
            .Require(UserProfile.MustBeActive)
            .ConfigureAwait(false);
        if (chat.OwnerIds.Contains(userProfile.Id)) {
            throw new NotSupportedException("The very last owner of the chat can't leave it.");
            // TODO: managing ownership functionality is required
            // check if there are other owners
            // if yes, remove current user from owners
            // otherwise delete the chat (telegram does it that way)
        }

        var leaveAuthorCommand = new IChatAuthorsBackend.ChangeHasLeftCommand(chatAuthor.Id, true);
        await Commander.Call(leaveAuthorCommand, cancellationToken).ConfigureAwait(false);
    }

    // Protected methods

    [ComputeMethod]
    protected virtual async Task<Chat?> GetPeerChat(Session session, string chatId, CancellationToken cancellationToken)
    {
        var parsedChatId = new ParsedChatId(chatId);
        if (!parsedChatId.IsValid)
            return null;

        switch (parsedChatId.Kind) {
        case ChatIdKind.PeerShort:
            chatId = await GetFullPeerChatId(session, chatId, cancellationToken).ConfigureAwait(false);
            return await GetPeerChat(session, chatId, cancellationToken).ConfigureAwait(false);
        case ChatIdKind.PeerFull:
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

        var user = await Auth.GetUser(session, cancellationToken).Require().ConfigureAwait(false);
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
    {
        var chatAuthor = await ChatAuthorsBackend.GetOrCreate(session, chatId, cancellationToken).ConfigureAwait(false);
        if (chatAuthor.HasLeft) {
            var command = new IChatAuthorsBackend.ChangeHasLeftCommand(chatAuthor.Id, false);
            await Commander.Call(command, cancellationToken).ConfigureAwait(false);
        }
        if (await IsInvited(session, chatId, cancellationToken).ConfigureAwait(false)) {
            var updateOptionCommand1 = new ISessionOptionsBackend.UpsertCommand(
                session,
                new(InvitedChatIdOptionKey, Symbol.Empty));
            await Commander.Call(updateOptionCommand1, true, cancellationToken).ConfigureAwait(false);

            var updateOptionCommand2 = new ISessionOptionsBackend.UpsertCommand(
                session,
                new(InvitedIdOptionKey, Symbol.Empty));
            await Commander.Call(updateOptionCommand2, true, cancellationToken).ConfigureAwait(false);
        }
    }

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
        var rules = await GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        var hasPermissions = rules.Has(required);
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

        if (!options.Items.TryGetValue(InvitedChatIdOptionKey, out var inviteChatId))
            return false;
        return OrdinalEquals(chatId, inviteChatId as string);
    }
}
