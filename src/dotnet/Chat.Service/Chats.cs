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
    private IAccounts Accounts { get; }
    private IAccountsBackend AccountsBackend { get; }
    private IChatAuthors ChatAuthors { get; }
    private IChatAuthorsBackend ChatAuthorsBackend { get; }
    private IUserContactsBackend UserContactsBackend { get; }
    private IChatsBackend Backend { get; }

    public Chats(IServiceProvider services) : base(services)
    {
        Auth = Services.GetRequiredService<IAuth>();
        Accounts = Services.GetRequiredService<IAccounts>();
        AccountsBackend = Services.GetRequiredService<IAccountsBackend>();
        ChatAuthors = Services.GetRequiredService<IChatAuthors>();
        ChatAuthorsBackend = Services.GetRequiredService<IChatAuthorsBackend>();
        UserContactsBackend = Services.GetRequiredService<IUserContactsBackend>();
        Backend = Services.GetRequiredService<IChatsBackend>();
    }

    // [ComputeMethod]
    public virtual async Task<Chat?> Get(Session session, string chatId, CancellationToken cancellationToken)
    {
        var isPeerChat = new ParsedChatId(chatId).Kind.IsPeerAny();
        return isPeerChat
            ? await GetPeerChat(session, chatId, cancellationToken).ConfigureAwait(false)
            : await GetGroupChat(session, chatId, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<Chat>> List(
        Session session,
        CancellationToken cancellationToken)
    {
        var chatIds = await ChatAuthors.ListChatIds(session, cancellationToken).ConfigureAwait(false);
        var chats = await chatIds
            .Select(id => Get(session, id, cancellationToken))
            .Collect()
            .ConfigureAwait(false);
        return chats.SkipNullItems().OrderBy(x => x.Title).ToImmutableArray();
    }

    // [ComputeMethod]
    public virtual async Task<ChatTile> GetTile(
        Session session,
        string chatId,
        ChatEntryType entryType,
        Range<long> idTileRange,
        CancellationToken cancellationToken)
    {
        await RequirePermissions(session, chatId, ChatPermissions.Read, cancellationToken).ConfigureAwait(false);
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
        await RequirePermissions(session, chatId, ChatPermissions.Read, cancellationToken).ConfigureAwait(false);
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
        await RequirePermissions(session, chatId, ChatPermissions.Read, cancellationToken).ConfigureAwait(false);
        return await Backend.GetIdRange(chatId, entryType, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<Range<long>> GetLastIdTile0(
        Session session,
        string chatId,
        ChatEntryType entryType,
        CancellationToken cancellationToken)
    {
        await RequirePermissions(session, chatId, ChatPermissions.Read, cancellationToken).ConfigureAwait(false);
        return await Backend.GetLastIdTile0(chatId, entryType, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<Range<long>> GetLastIdTile1(
        Session session,
        string chatId,
        ChatEntryType entryType,
        CancellationToken cancellationToken)
    {
        await RequirePermissions(session, chatId, ChatPermissions.Read, cancellationToken).ConfigureAwait(false);
        return await Backend.GetLastIdTile1(chatId, entryType, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ChatAuthorRules> GetRules(
        Session session,
        string chatId,
        CancellationToken cancellationToken)
    {
        var chatPrincipalId = await ChatAuthors.GetPrincipalId(session, chatId, cancellationToken).ConfigureAwait(false);
        return await Backend.GetRules(chatId, chatPrincipalId, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<bool> CanJoin(Session session, string chatId, CancellationToken cancellationToken)
    {
        var author = await ChatAuthors.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        if (author is { HasLeft: false})
            return false;

        var rules = await GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        if (rules.CanJoin())
            return true;

        var invited = await IsInvited(session, chatId, cancellationToken).ConfigureAwait(false);
        return invited;
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<TextEntryAttachment>> GetTextEntryAttachments(
        Session session, string chatId, long entryId, CancellationToken cancellationToken)
    {
        await RequirePermissions(session, chatId, ChatPermissions.Read, cancellationToken).ConfigureAwait(false);
        return await Backend.GetTextEntryAttachments(chatId, entryId, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<bool> CanSendPeerChatMessage(Session session, string chatPrincipalId, CancellationToken cancellationToken)
    {
        var account = await Accounts.Get(session, cancellationToken).ConfigureAwait(false);
        if (account == null)
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
            if (chatAuthor == null || chatAuthor.UserId.IsEmpty || chatAuthor.UserId == account.Id)
                return false;
        }
        else {
            var otherUserId = parsedChatPrincipalId.UserId.Id;
            var otherAccount = await AccountsBackend.Get(otherUserId, cancellationToken).ConfigureAwait(false);
            if (otherAccount == null || otherAccount.Id == account.Id)
                return false;
        }
        return true;
    }

    // [ComputeMethod]
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

    // [ComputeMethod]
    public virtual async Task<UserContact?> GetPeerChatContact(Session session, Symbol chatId, CancellationToken cancellationToken)
    {
        var account = await Accounts.Get(session, cancellationToken).ConfigureAwait(false);
        if (account == null)
            return null;

        var (_, contact) = await GetPeerChatContact(chatId, account.Id, cancellationToken).ConfigureAwait(false);
        return contact;
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<Author>> ListMentionableAuthors(Session session, string chatId, CancellationToken cancellationToken)
    {
        await RequirePermissions(session, chatId, ChatPermissions.Read, cancellationToken).ConfigureAwait(false);
        var chatAuthorIds = await ChatAuthorsBackend.ListAuthorIds(chatId, cancellationToken).ConfigureAwait(false);
        var authors = await chatAuthorIds
            .Select(id => ChatAuthors.GetAuthor(session, chatId, id, true, cancellationToken))
            .Collect()
            .ConfigureAwait(false);
        return authors
            .SkipNullItems()
            .OrderBy(a => a.Name)
            .ToImmutableArray();
    }

    // [CommandHandler]
    public virtual async Task<Chat?> ChangeChat(IChats.ChangeChatCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return default!; // It just spawns other commands, so nothing to do here

        var (session, chatId, expectedVersion, change) = command;

        var cmd = new IChatsBackend.ChangeChatCommand(chatId, expectedVersion, change.RequireValid());
        if (change.Create.HasValue) {
            var account = await Accounts.Get(session, cancellationToken)
                .Require(Account.MustBeActive)
                .ConfigureAwait(false);
            cmd = cmd with { CreatorUserId = account.Id };
        }
        if (change.Update.HasValue)
            await RequirePermissions(session, chatId, ChatPermissions.EditProperties, cancellationToken).ConfigureAwait(false);
        var chat = await Commander.Call(cmd, true, cancellationToken).ConfigureAwait(false);
        return chat;
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
        return default;
    }

    // [CommandHandler]
    public virtual Task<ChatEntry> UpsertTextEntry(IChats.UpsertTextEntryCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return Task.FromResult<ChatEntry>(null!); // It just spawns other commands, so nothing to do here

        return command.Id != null
            ? UpdateTextEntry(command, cancellationToken)
            : CreateTextEntry(command, cancellationToken);
    }

    // [CommandHandler]
    public virtual async Task RemoveTextEntry(IChats.RemoveTextEntryCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var (session, chatId, entryId) = command;
        var entry = await GetChatEntry(session,
                chatId,
                entryId,
                ChatEntryType.Text,
                cancellationToken)
            .ConfigureAwait(false);
        await AssertCanRemoveTextEntry(entry, session, cancellationToken).ConfigureAwait(false);

        var textEntry = await RemoveChatEntry(session, chatId, entryId, ChatEntryType.Text, cancellationToken).ConfigureAwait(false);

        if (textEntry.AudioEntryId != null)
            await RemoveChatEntry(session, chatId, textEntry.AudioEntryId.Value, ChatEntryType.Audio, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task LeaveChat(IChats.LeaveChatCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var (session, chatId) = command;
        await RequirePermissions(session, chatId, ChatPermissions.Leave, cancellationToken).ConfigureAwait(false);

        var chat = await Get(session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return;
        var chatAuthor = await ChatAuthors.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        if (chatAuthor == null || chatAuthor.HasLeft)
            return;

        var leaveAuthorCommand = new IChatAuthorsBackend.ChangeHasLeftCommand(chatAuthor.Id, true);
        await Commander.Call(leaveAuthorCommand, cancellationToken).ConfigureAwait(false);
    }

    // Protected methods

    [ComputeMethod]
    protected virtual async Task<Chat?> GetGroupChat(Session session, string chatId, CancellationToken cancellationToken)
    {
        var canRead = await HasPermissions(session, chatId, ChatPermissions.Read, cancellationToken)
            .ConfigureAwait(false);
        if (!canRead)
            return null;
        return await Backend.Get(chatId, cancellationToken).ConfigureAwait(false);
    }

    [ComputeMethod]
    protected virtual async Task<Chat?> GetPeerChat(Session session, string chatId, CancellationToken cancellationToken)
    {
        var parsedChatId = new ParsedChatId(chatId).AssertValid();

        switch (parsedChatId.Kind) {
        case ChatIdKind.PeerShort:
            var fullChatId = await GetFullPeerChatId(session, chatId, cancellationToken).ConfigureAwait(false);
            if (fullChatId.IsNullOrEmpty())
                return null;
            return await GetPeerChat(session, fullChatId, cancellationToken).ConfigureAwait(false);
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

        var account = await Accounts.Get(session, cancellationToken).ConfigureAwait(false);
        if (account == null)
            return null;

        var (contactUserId, contact) = await GetPeerChatContact(chatId, account.Id, cancellationToken).ConfigureAwait(false);
        var title = contact?.Name ?? await UserContactsBackend.SuggestContactName(contactUserId, cancellationToken).ConfigureAwait(false);
        chat = chat with { Title = title };
        return chat;
    }

    [ComputeMethod]
    protected virtual async Task<string?> GetFullPeerChatId(Session session, string chatId, CancellationToken cancellationToken)
    {
        if (chatId.IsNullOrEmpty())
            throw new ArgumentOutOfRangeException(nameof(chatId));

        var parsedChatId = new ParsedChatId(chatId).AssertValid();

        switch (parsedChatId.Kind) {
        case ChatIdKind.PeerFull:
            return chatId;
        case ChatIdKind.PeerShort:
            var account = await Accounts.Get(session, cancellationToken).ConfigureAwait(false);
            if (account == null)
                return null;
            return ParsedChatId.FormatFullPeerChatId(account.Id, parsedChatId.UserId1);
        default:
            throw new ArgumentOutOfRangeException(nameof(chatId));
        }
    }

    [ComputeMethod]
    protected virtual async Task<(string TargetUserId, UserContact? Contact)> GetPeerChatContact(
        string chatId, Symbol ownerUserId, CancellationToken cancellationToken)
    {
        var parsedChatId = new ParsedChatId(chatId).AssertPeerFull();
        var (userId1, userId2) = (parsedChatId.UserId1.Id, parsedChatId.UserId2.Id);

        var targetUserId = (userId1, userId2).OtherThan(ownerUserId);
        if (targetUserId.IsEmpty)
            throw StandardError.Constraint("Specified peer chat doesn't belong to the current user.");

        var contact = await UserContactsBackend.Get(ownerUserId, targetUserId, cancellationToken).ConfigureAwait(false);
        return (targetUserId, contact);
    }

    // Private methods

    private async Task<ChatEntry> GetChatEntry(Session session, string chatId, long entryId, ChatEntryType type, CancellationToken cancellationToken)
    {
        var idTile = IdTileStack.FirstLayer.GetTile(entryId);
        var tile = await GetTile(session, chatId, type, idTile.Range, cancellationToken)
            .ConfigureAwait(false);
        var chatEntry = tile.Entries.Single(e => e.Id == entryId);
        return chatEntry;
    }

    private async Task<ChatEntry> CreateTextEntry(
        IChats.UpsertTextEntryCommand command,
        CancellationToken cancellationToken)
    {
        var (session, chatId, _, text) = command;
        // NOTE(AY): Temp. commented this out, coz it confuses lots of people who're trying to post in anonymous mode
        // await AssertHasPermissions(session, chatId, ChatPermissions.Write, cancellationToken).ConfigureAwait(false);
        await RequirePermissions(session, chatId, ChatPermissions.Write, cancellationToken).ConfigureAwait(false);

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

    private async Task<ChatEntry> UpdateTextEntry(
        IChats.UpsertTextEntryCommand command,
        CancellationToken cancellationToken)
    {
        var (session, chatId, id, text) = command;
        var chatEntry = await GetChatEntry(session,
                chatId,
                id!.Value,
                ChatEntryType.Text,
                cancellationToken)
            .ConfigureAwait(false);

        await AssertCanUpdateTextEntry(chatEntry, session, cancellationToken).ConfigureAwait(false);

        chatEntry = chatEntry with { Content = text };
        var upsertCommand = new IChatsBackend.UpsertEntryCommand(chatEntry);
        return await Commander.Call(upsertCommand, cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private async Task<ChatEntry> RemoveChatEntry(Session session, string chatId, long entryId, ChatEntryType type, CancellationToken cancellationToken)
    {
        var chatEntry = await GetChatEntry(session, chatId, entryId, type, cancellationToken).ConfigureAwait(false);

        var author = await ChatAuthorsBackend.GetOrCreate(session, chatId, cancellationToken).ConfigureAwait(false);
        if (chatEntry.AuthorId != author.Id)
            throw StandardError.Unauthorized("You can delete only your own messages.");

        chatEntry = chatEntry with { IsRemoved = true };
        var upsertCommand = new IChatsBackend.UpsertEntryCommand(chatEntry);
        await Commander.Call(upsertCommand, true, cancellationToken).ConfigureAwait(false);
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

    // Assertions & permission checks

    private async Task AssertCanUpdateTextEntry(
        ChatEntry chatEntry,
        Session session,
        CancellationToken cancellationToken)
    {
        await RequirePermissions(session, chatEntry.ChatId, ChatPermissions.Write, cancellationToken).ConfigureAwait(false);

        if (chatEntry.IsRemoved)
            throw StandardError.NotFound<ChatEntry>();

        var author = await ChatAuthors.Get(session, chatEntry.ChatId, cancellationToken).Require().ConfigureAwait(false);
        if (chatEntry.AuthorId != author.Id)
            throw StandardError.Unauthorized("User can edit only their own messages.");

        if (chatEntry.Type != ChatEntryType.Text || !chatEntry.StreamId.IsEmpty)
            throw StandardError.Constraint("Only text messages can be edited.");
    }

    private async Task AssertCanRemoveTextEntry(
        ChatEntry chatEntry,
        Session session,
        CancellationToken cancellationToken)
    {
        await RequirePermissions(session, chatEntry.ChatId, ChatPermissions.Write, cancellationToken).ConfigureAwait(false);

        if (chatEntry.IsRemoved)
            throw StandardError.NotFound<ChatEntry>();

        var author = await ChatAuthors.Get(session, chatEntry.ChatId, cancellationToken).Require().ConfigureAwait(false);
        if (chatEntry.AuthorId != author.Id)
            throw StandardError.Unauthorized("User can remove only their own messages.");

        if (chatEntry.IsStreaming)
            throw StandardError.Constraint("This chat entry is streaming.");
    }

    private async Task RequirePermissions(
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

    private async Task<bool> IsInvited(
        Session session,
        string chatId,
        CancellationToken cancellationToken)
    {
        var options = await Auth.GetOptions(session, cancellationToken).ConfigureAwait(false);
        return options.Items.TryGetValue(InvitedChatIdOptionKey, out var inviteChatId)
            && OrdinalEquals(chatId, inviteChatId as string);
    }
}
