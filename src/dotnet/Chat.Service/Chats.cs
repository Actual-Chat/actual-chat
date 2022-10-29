using ActualChat.Chat.Db;
using ActualChat.Commands;
using ActualChat.Invite;
using ActualChat.Kvas;
using ActualChat.Users;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Chat;

public class Chats : DbServiceBase<ChatDbContext>, IChats
{
    private static readonly TileStack<long> IdTileStack = Constants.Chat.IdTileStack;

    private IAccounts Accounts { get; }
    private IAccountsBackend AccountsBackend { get; }
    private IAuthors Authors { get; }
    private IAuthorsBackend AuthorsBackend { get; }
    private IContactsBackend ContactsBackend { get; }
    private IServerKvas ServerKvas { get; }
    private IChatsBackend Backend { get; }

    public Chats(IServiceProvider services) : base(services)
    {
        Accounts = services.GetRequiredService<IAccounts>();
        AccountsBackend = services.GetRequiredService<IAccountsBackend>();
        Authors = services.GetRequiredService<IAuthors>();
        AuthorsBackend = services.GetRequiredService<IAuthorsBackend>();
        ContactsBackend = services.GetRequiredService<IContactsBackend>();
        ServerKvas = services.ServerKvas();
        Backend = services.GetRequiredService<IChatsBackend>();
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
        var chatIds = await Authors.ListChatIds(session, cancellationToken).ConfigureAwait(false);
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
        return await Backend.GetIdRange(chatId, entryType, false, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<AuthorRules> GetRules(
        Session session,
        string chatId,
        CancellationToken cancellationToken)
    {
        var principalId = await GetOwnPrincipalId(session, chatId, cancellationToken).ConfigureAwait(false);
        return await Backend.GetRules(chatId, principalId, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ChatSummary?> GetSummary(
        Session session,
        string chatId,
        CancellationToken cancellationToken)
    {
        var canRead = await HasPermissions(session, chatId, ChatPermissions.Read, cancellationToken).ConfigureAwait(false);
        if (!canRead)
            return null;
        return await Backend.GetSummary(chatId, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<bool> CanJoin(Session session, string chatId, CancellationToken cancellationToken)
    {
        var author = await Authors.GetOwn(session, chatId, cancellationToken).ConfigureAwait(false);
        if (author is { HasLeft: false })
            return false;

        var rules = await GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        if (rules.CanJoin())
            return true;

        var hasInvite = await HasInvite(session, chatId, cancellationToken).ConfigureAwait(false);
        return hasInvite;
    }

    // [ComputeMethod]
    public virtual async Task<bool> CanPeerChat(
        Session session, string chatId, string authorId,
        CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account == null)
            return false;

        var parsedAuthorId = new ParsedAuthorId(authorId);
        if (!parsedAuthorId.IsValid || parsedAuthorId.ChatId.Id != chatId)
            return false;

        if (!await HasPermissions(session, chatId, ChatPermissions.Read, cancellationToken).ConfigureAwait(false))
            return false;

        var author = await AuthorsBackend.Get(chatId, authorId, cancellationToken).ConfigureAwait(false);
        if (author == null || author.UserId.IsEmpty || author.UserId == account.Id)
            return false;

        return true;
    }

    // [ComputeMethod]
    public virtual async Task<string?> GetPeerChatId(Session session, string chatId, string authorId, CancellationToken cancellationToken)
    {
        if (!await CanPeerChat(session, chatId, authorId, cancellationToken).ConfigureAwait(false))
            return null;

        var userId2 = await AuthorsBackend.GetUserId(chatId, authorId, cancellationToken).ConfigureAwait(false);
        if (userId2.IsEmpty)
            return null;

        return ParsedChatId.FormatShortPeerChatId(userId2);
    }

    // [ComputeMethod]
    public virtual async Task<Contact?> GetPeerChatContact(Session session, string chatId, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account == null)
            return null;

        var contact = await GetPeerChatContact(chatId, account.Id, cancellationToken).ConfigureAwait(false);
        return contact;
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<Author>> ListMentionableAuthors(Session session, string chatId, CancellationToken cancellationToken)
    {
        await RequirePermissions(session, chatId, ChatPermissions.Read, cancellationToken).ConfigureAwait(false);
        var authorIds = await AuthorsBackend.ListAuthorIds(chatId, cancellationToken).ConfigureAwait(false);
        var authors = await authorIds
            .Select(id => Authors.Get(session, chatId, id, cancellationToken))
            .Collect()
            .ConfigureAwait(false);
        return authors
            .SkipNullItems()
            .OrderBy(a => a.Avatar.Name)
            .ToImmutableArray();
    }

    // Not a [ComputeMethod]!
    public virtual async Task<ChatEntry?> FindNext(
        Session session,
        string chatId,
        long? startEntryId,
        string text,
        CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account is null)
            return null;

        text.RequireMaxLength(Constants.Chat.MaxSearchFilterLength, "text.length");

        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);

        var dbEntry = await dbContext.ChatEntries.OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(x => (startEntryId == null || x.Id < startEntryId) && x.Content.Contains(text), cancellationToken)
            .ConfigureAwait(false);
        return dbEntry?.ToModel();
    }

    // [CommandHandler]
    public virtual async Task<Chat> Change(IChats.ChangeCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return default!; // It just spawns other commands, so nothing to do here

        var (session, chatId, expectedVersion, change) = command;

        var changeCommand = new IChatsBackend.ChangeCommand(chatId, expectedVersion, change.RequireValid());
        if (change.Create.HasValue) {
            var account = await Accounts.GetOwn(session, cancellationToken)
                .Require(AccountFull.MustBeActive)
                .ConfigureAwait(false);
            changeCommand = changeCommand with {
                CreatorUserId = account.Id,
            };
        }
        if (change.Update.HasValue)
            await RequirePermissions(session, chatId, ChatPermissions.EditProperties, cancellationToken).ConfigureAwait(false);
        var chat = await Commander.Call(changeCommand, true, cancellationToken).ConfigureAwait(false);
        return chat;
    }

    // [CommandHandler]
    public virtual async Task<Unit> Join(IChats.JoinCommand command, CancellationToken cancellationToken)
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
    public virtual async Task Leave(IChats.LeaveCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var (session, chatId) = command;
        await RequirePermissions(session, chatId, ChatPermissions.Leave, cancellationToken).ConfigureAwait(false);

        var chat = await Get(session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return;

        var author = await Authors.GetOwn(session, chatId, cancellationToken).ConfigureAwait(false);
        if (author == null || author.HasLeft)
            return;

        var leaveAuthorCommand = new IAuthorsBackend.ChangeHasLeftCommand(chatId, author.Id, true);
        await Commander.Call(leaveAuthorCommand, cancellationToken).ConfigureAwait(false);
    }

    // Protected methods

    [ComputeMethod]
    protected virtual async Task<Chat?> GetGroupChat(Session session, string chatId, CancellationToken cancellationToken)
    {
        var canRead = await HasPermissions(session, chatId, ChatPermissions.Read, cancellationToken).ConfigureAwait(false);
        if (!canRead)
            return null;

        return await Backend.Get(chatId, cancellationToken).ConfigureAwait(false);
    }

    [ComputeMethod]
    protected virtual async Task<Chat?> GetPeerChat(Session session, string chatId, CancellationToken cancellationToken)
    {
        var parsedChatId = new ParsedChatId(chatId);
        switch (parsedChatId.Kind) {
        case ChatIdKind.PeerShort:
            var fullChatId = await GetFullPeerChatId(session, chatId, cancellationToken).ConfigureAwait(false);
            if (fullChatId.IsNullOrEmpty())
                return null;
            return await GetPeerChat(session, fullChatId, cancellationToken).ConfigureAwait(false);
        case ChatIdKind.PeerFull:
            break;
        default: // Group or Invalid
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

        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account == null)
            return null;

        var contact = await GetPeerChatContact(chatId, account.Id, cancellationToken).ConfigureAwait(false);
        if (contact == null)
            return null;

        chat = chat with { Title = contact.Avatar.Name };
        return chat;
    }

    [ComputeMethod]
    protected virtual async Task<string?> GetFullPeerChatId(Session session, string chatId, CancellationToken cancellationToken)
    {
        var parsedChatId = new ParsedChatId(chatId);
        switch (parsedChatId.Kind) {
        case ChatIdKind.PeerFull:
            return chatId;
        case ChatIdKind.PeerShort:
            var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
            if (account == null)
                return null;
            return ParsedChatId.FormatFullPeerChatId(account.Id, parsedChatId.UserId1);
        default: // Group or Invalid
            throw new ArgumentOutOfRangeException(nameof(chatId));
        }
    }

    [ComputeMethod]
    protected virtual async Task<Contact?> GetPeerChatContact(
        string chatId, string ownerUserId, CancellationToken cancellationToken)
    {
        var parsedChatId = new ParsedChatId(chatId);
        switch (parsedChatId.Kind) {
        case ChatIdKind.PeerFull:
            break;
        case ChatIdKind.PeerShort:
            parsedChatId = chatId = ParsedChatId.FormatFullPeerChatId(ownerUserId, parsedChatId.UserId1);
            break;
        default: // Group or Invalid
            return null;
        }

        var (userId1, userId2) = (parsedChatId.UserId1.Id, parsedChatId.UserId2.Id);
        var targetUserId = (userId1, userId2).OtherThan((Symbol)ownerUserId);
        if (targetUserId.IsEmpty)
            throw StandardError.Constraint("Specified peer chat doesn't belong to the current user.");

        var contact = await ContactsBackend.Get(ownerUserId, targetUserId, cancellationToken).ConfigureAwait(false);
        return contact;
    }

    // Private methods

    public virtual async Task<Symbol> GetOwnPrincipalId(
        Session session, string chatId,
        CancellationToken cancellationToken)
    {
        var author = await Authors.GetOwn(session, chatId, cancellationToken).ConfigureAwait(false);
        if (author != null)
            return author.Id;

        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        return account?.Id ?? Symbol.Empty;
    }

    private async Task<ChatEntry> GetChatEntry(
        Session session, string chatId, long entryId, ChatEntryType type,
        CancellationToken cancellationToken)
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

        var author = await AuthorsBackend.GetOrCreate(session, chatId, cancellationToken).ConfigureAwait(false);
        var chatEntry = new ChatEntry {
            ChatId = chatId,
            AuthorId = author.Id,
            Content = text,
            Type = ChatEntryType.Text,
            RepliedChatEntryId = command.RepliedChatEntryId!,
        };
        var upsertCommand = new IChatsBackend.UpsertEntryCommand(chatEntry, command.Attachments.Length > 0);
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

    private async Task<ChatEntry> RemoveChatEntry(Session session, string chatId, long entryId, ChatEntryType type, CancellationToken cancellationToken)
    {
        var chatEntry = await GetChatEntry(session, chatId, entryId, type, cancellationToken).ConfigureAwait(false);

        var author = await AuthorsBackend.GetOrCreate(session, chatId, cancellationToken).ConfigureAwait(false);
        if (chatEntry.AuthorId != author.Id)
            throw StandardError.Unauthorized("You can delete only your own messages.");

        chatEntry = chatEntry with { IsRemoved = true };
        var upsertCommand = new IChatsBackend.UpsertEntryCommand(chatEntry);
        await Commander.Call(upsertCommand, true, cancellationToken).ConfigureAwait(false);
        return chatEntry;
    }

    private async Task JoinChat(Session session, string chatId, CancellationToken cancellationToken)
    {
        var author = await AuthorsBackend.GetOrCreate(session, chatId, cancellationToken).ConfigureAwait(false);
        if (author.HasLeft) {
            var command = new IAuthorsBackend.ChangeHasLeftCommand(chatId, author.Id, false);
            await Commander.Call(command, cancellationToken).ConfigureAwait(false);
        }
        var hasInvite = await HasInvite(session, chatId, cancellationToken).ConfigureAwait(false);
        if (hasInvite)
            // Remove the invite
            new IServerKvas.SetCommand(session, ServerKvasInviteKey.ForChat(chatId), null)
                .EnqueueOnCompletion(Queues.Users);
    }

    // Assertions & permission checks

    private async ValueTask AssertCanUpdateTextEntry(
        ChatEntry chatEntry,
        Session session,
        CancellationToken cancellationToken)
    {
        await RequirePermissions(session, chatEntry.ChatId, ChatPermissions.Write, cancellationToken).ConfigureAwait(false);

        if (chatEntry.IsRemoved)
            throw StandardError.NotFound<ChatEntry>();

        var author = await Authors.GetOwn(session, chatEntry.ChatId, cancellationToken).Require().ConfigureAwait(false);
        if (chatEntry.AuthorId != author.Id)
            throw StandardError.Unauthorized("User can edit only their own messages.");

        if (chatEntry.Type != ChatEntryType.Text || !chatEntry.StreamId.IsEmpty)
            throw StandardError.Constraint("Only text messages can be edited.");
    }

    private async ValueTask AssertCanRemoveTextEntry(
        ChatEntry chatEntry,
        Session session,
        CancellationToken cancellationToken)
    {
        await RequirePermissions(session, chatEntry.ChatId, ChatPermissions.Write, cancellationToken).ConfigureAwait(false);

        if (chatEntry.IsRemoved)
            throw StandardError.NotFound<ChatEntry>();

        var author = await Authors.GetOwn(session, chatEntry.ChatId, cancellationToken).Require().ConfigureAwait(false);
        if (chatEntry.AuthorId != author.Id)
            throw StandardError.Unauthorized("User can remove only their own messages.");

        if (chatEntry.IsStreaming)
            throw StandardError.Constraint("This chat entry is streaming.");
    }

    private async ValueTask RequirePermissions(
        Session session, string chatId, ChatPermissions required,
        CancellationToken cancellationToken)
    {
        if (!await HasPermissions(session, chatId, required, cancellationToken).ConfigureAwait(false))
            throw ChatPermissionsExt.NotEnoughPermissions(required);
    }

    private async ValueTask<bool> HasPermissions(
        Session session, string chatId, ChatPermissions required,
        CancellationToken cancellationToken)
    {
        var rules = await GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        var hasPermissions = rules.Has(required);
        if (!hasPermissions && required == ChatPermissions.Read) {
            // NOTE(AY): Maybe makes sense to move this to UI code - i.e. process the invite there
            // in case there is not enough permissions & retry.
            return await HasInvite(session, chatId, cancellationToken).ConfigureAwait(false);
        }
        return hasPermissions;
    }

    private async ValueTask<bool> HasInvite(
        Session session,
        string chatId,
        CancellationToken cancellationToken)
    {
        var value = await ServerKvas.Get(session, ServerKvasInviteKey.ForChat(chatId), cancellationToken).ConfigureAwait(false);
        return value.HasValue;
    }
}
