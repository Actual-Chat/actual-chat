using ActualChat.Chat.Db;
using ActualChat.Commands;
using ActualChat.Contacts;
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
    private IAuthors Authors { get; }
    private IAuthorsBackend AuthorsBackend { get; }
    private IContactsBackend ContactsBackend { get; }
    private IServerKvas ServerKvas { get; }
    private IChatsBackend Backend { get; }

    public Chats(IServiceProvider services) : base(services)
    {
        Accounts = services.GetRequiredService<IAccounts>();
        Authors = services.GetRequiredService<IAuthors>();
        AuthorsBackend = services.GetRequiredService<IAuthorsBackend>();
        ContactsBackend = services.GetRequiredService<IContactsBackend>();
        ServerKvas = services.ServerKvas();
        Backend = services.GetRequiredService<IChatsBackend>();
    }

    // [ComputeMethod]
    public virtual async Task<Chat?> Get(Session session, string chatId, CancellationToken cancellationToken)
    {
        var isPeerChat = new ChatId(chatId).Kind == ChatKind.Peer;
        return isPeerChat
            ? await GetPeerChat(session, chatId, cancellationToken).ConfigureAwait(false)
            : await GetGroupChat(session, chatId, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ChatTile> GetTile(
        Session session,
        string chatId,
        ChatEntryKind entryKind,
        Range<long> idTileRange,
        CancellationToken cancellationToken)
    {
        await this.RequirePermissions(session, chatId, ChatPermissions.Read, cancellationToken).ConfigureAwait(false);
        return await Backend.GetTile(chatId, entryKind, idTileRange, false, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<long> GetEntryCount(
        Session session,
        string chatId,
        ChatEntryKind entryKind,
        Range<long>? idTileRange,
        CancellationToken cancellationToken)
    {
        await this.RequirePermissions(session, chatId, ChatPermissions.Read, cancellationToken).ConfigureAwait(false);
        return await Backend.GetEntryCount(chatId, entryKind, idTileRange, false, cancellationToken).ConfigureAwait(false);
    }

    // Note that it returns (firstId, lastId + 1) range!
    // [ComputeMethod]
    public virtual async Task<Range<long>> GetIdRange(
        Session session,
        string chatId,
        ChatEntryKind entryKind,
        CancellationToken cancellationToken)
    {
        await this.RequirePermissions(session, chatId, ChatPermissions.Read, cancellationToken).ConfigureAwait(false);
        return await Backend.GetIdRange(chatId, entryKind, false, cancellationToken).ConfigureAwait(false);
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
        var canRead = await this.HasPermissions(session, chatId, ChatPermissions.Read, cancellationToken).ConfigureAwait(false);
        if (!canRead)
            return null;

        return await Backend.GetSummary(chatId, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<bool> HasInvite(Session session, string chatId, CancellationToken cancellationToken)
    {
        var value = await ServerKvas.Get(session, ServerKvasInviteKey.ForChat(chatId), cancellationToken).ConfigureAwait(false);
        return value.HasValue;
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
    public virtual async Task<ImmutableArray<Author>> ListMentionableAuthors(Session session, string chatId, CancellationToken cancellationToken)
    {
        await this.RequirePermissions(session, chatId, ChatPermissions.Read, cancellationToken).ConfigureAwait(false);
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

        var dbEntry = await dbContext.ChatEntries.OrderByDescending(x => x.LocalId)
            .FirstOrDefaultAsync(x => (startEntryId == null || x.LocalId < startEntryId) && x.Content.Contains(text), cancellationToken)
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
            await this.RequirePermissions(session, chatId, ChatPermissions.EditProperties, cancellationToken).ConfigureAwait(false);
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

        return command.LocalId != null
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
                ChatEntryKind.Text,
                cancellationToken)
            .ConfigureAwait(false);
        await AssertCanRemoveTextEntry(entry, session, cancellationToken).ConfigureAwait(false);

        var textEntry = await RemoveChatEntry(session, chatId, entryId, ChatEntryKind.Text, cancellationToken).ConfigureAwait(false);

        if (textEntry.AudioEntryId != null)
            await RemoveChatEntry(session, chatId, textEntry.AudioEntryId.Value, ChatEntryKind.Audio, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task Leave(IChats.LeaveCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var (session, chatId) = command;
        await this.RequirePermissions(session, chatId, ChatPermissions.Leave, cancellationToken).ConfigureAwait(false);

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
        var canRead = await this.HasPermissions(session, chatId, ChatPermissions.Read, cancellationToken).ConfigureAwait(false);
        if (!canRead)
            return null;

        return await Backend.Get(chatId, cancellationToken).ConfigureAwait(false);
    }

    [ComputeMethod]
    protected virtual async Task<Chat?> GetPeerChat(Session session, string chatId, CancellationToken cancellationToken)
    {
        var parsedChatId = new ChatId(chatId);
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account == null)
            return null;

        if (!parsedChatId.IsPeerChatId(account.Id, out var otherUserId))
            return null;

        var contact = await ContactsBackend.GetForUser(account.Id, otherUserId, cancellationToken).ConfigureAwait(false);
        return contact?.Chat;
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
        Session session, string chatId, long localId, ChatEntryKind kind,
        CancellationToken cancellationToken)
    {
        var idTile = IdTileStack.FirstLayer.GetTile(localId);
        var tile = await GetTile(session, chatId, kind, idTile.Range, cancellationToken)
            .ConfigureAwait(false);
        var chatEntry = tile.Entries.Single(e => e.LocalId == localId);
        return chatEntry;
    }

    private async Task<ChatEntry> CreateTextEntry(
        IChats.UpsertTextEntryCommand command,
        CancellationToken cancellationToken)
    {
        var (session, chatId, _, text, repliedChatEntryId) = command;
        // NOTE(AY): Temp. commented this out, coz it confuses lots of people who're trying to post in anonymous mode
        // await AssertHasPermissions(session, chatId, ChatPermissions.Write, cancellationToken).ConfigureAwait(false);
        await this.RequirePermissions(session, chatId, ChatPermissions.Write, cancellationToken).ConfigureAwait(false);

        var author = await AuthorsBackend.GetOrCreate(session, chatId, cancellationToken).ConfigureAwait(false);
        var chatEntry = new ChatEntry {
            Id = new ChatEntryId(chatId, ChatEntryKind.Text, 0, Parse.None),
            AuthorId = author.Id,
            Content = text,
            RepliedChatEntryId = repliedChatEntryId.IsSome(out var v) ? v : null,
        };
        var upsertCommand = new IChatsBackend.UpsertEntryCommand(chatEntry, command.Attachments.Length > 0);
        var textEntry =  await Commander.Call(upsertCommand, true, cancellationToken).ConfigureAwait(false);

        for (var index = 0; index < command.Attachments.Length; index++) {
            var attachmentUpload = command.Attachments[index];
            var (fileName, content, contentType) = attachmentUpload;
            var contentLocalId = Ulid.NewUlid().ToString();
            var contentId = $"attachments/{chatId}/{contentLocalId}/{fileName}";

            var saveCommand = new IContentSaverBackend.SaveContentCommand(contentId, content, contentType);
            await Commander.Call(saveCommand, true, cancellationToken).ConfigureAwait(false);

            var attachment = new TextEntryAttachment {
                EntryId = textEntry.Id,
                Index = index,
                Length = content.Length,
                ContentType = contentType,
                FileName = fileName,
                ContentId = contentId,
                Width = attachmentUpload.Width,
                Height = attachmentUpload.Height,
            };
            var createAttachmentCommand = new IChatsBackend.CreateAttachmentCommand(attachment);
            await Commander.Call(createAttachmentCommand, true, cancellationToken).ConfigureAwait(false);
        }
        return textEntry;
    }

    private async Task<ChatEntry> UpdateTextEntry(
        IChats.UpsertTextEntryCommand command,
        CancellationToken cancellationToken)
    {
        var (session, chatId, id, text, repliedChatEntryId) = command;
        var chatEntry = await GetChatEntry(session,
                chatId,
                id!.Value,
                ChatEntryKind.Text,
                cancellationToken)
            .ConfigureAwait(false);

        await AssertCanUpdateTextEntry(chatEntry, session, cancellationToken).ConfigureAwait(false);

        chatEntry = chatEntry with { Content = text };
        if (repliedChatEntryId.IsSome(out var v))
            chatEntry = chatEntry with { RepliedChatEntryId = v };
        var upsertCommand = new IChatsBackend.UpsertEntryCommand(chatEntry);
        return await Commander.Call(upsertCommand, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ChatEntry> RemoveChatEntry(Session session, string chatId, long entryId, ChatEntryKind kind, CancellationToken cancellationToken)
    {
        var chatEntry = await GetChatEntry(session, chatId, entryId, kind, cancellationToken).ConfigureAwait(false);

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
            var command = new IAuthorsBackend.ChangeHasLeftCommand(new ChatId(chatId), author.Id, false);
            await Commander.Call(command, cancellationToken).ConfigureAwait(false);
        }
        var hasInvite = await HasInvite(session, chatId, cancellationToken).ConfigureAwait(false);
        if (hasInvite)
            // Remove the invite
            new IServerKvas.SetCommand(session, ServerKvasInviteKey.ForChat(chatId), null)
                .EnqueueOnCompletion(Queues.Users.ShardBy(author.UserId));
    }

    // Assertions

    private async ValueTask AssertCanUpdateTextEntry(
        ChatEntry chatEntry,
        Session session,
        CancellationToken cancellationToken)
    {
        await this.RequirePermissions(session, chatEntry.ChatId, ChatPermissions.Write, cancellationToken).ConfigureAwait(false);

        if (chatEntry.IsRemoved)
            throw StandardError.NotFound<ChatEntry>();

        var author = await Authors.GetOwn(session, chatEntry.ChatId, cancellationToken).Require().ConfigureAwait(false);
        if (chatEntry.AuthorId != author.Id)
            throw StandardError.Unauthorized("User can edit only their own messages.");

        if (chatEntry.Kind != ChatEntryKind.Text || !chatEntry.StreamId.IsEmpty)
            throw StandardError.Constraint("Only text messages can be edited.");
    }

    private async ValueTask AssertCanRemoveTextEntry(
        ChatEntry chatEntry,
        Session session,
        CancellationToken cancellationToken)
    {
        await this.RequirePermissions(session, chatEntry.ChatId, ChatPermissions.Write, cancellationToken).ConfigureAwait(false);

        if (chatEntry.IsRemoved)
            throw StandardError.NotFound<ChatEntry>();

        var author = await Authors.GetOwn(session, chatEntry.ChatId, cancellationToken).Require().ConfigureAwait(false);
        if (chatEntry.AuthorId != author.Id)
            throw StandardError.Unauthorized("User can remove only their own messages.");

        if (chatEntry.IsStreaming)
            throw StandardError.Constraint("This chat entry is streaming.");
    }
}
