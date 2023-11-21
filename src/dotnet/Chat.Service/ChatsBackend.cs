using ActualChat.Chat.Db;
using ActualChat.Chat.Events;
using ActualChat.Db;
using ActualChat.Hosting;
using ActualChat.Commands;
using ActualChat.Media;
using ActualChat.Users;
using ActualChat.Users.Events;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Chat;

public class ChatsBackend(IServiceProvider services) : DbServiceBase<ChatDbContext>(services), IChatsBackend
{
    private static readonly TileStack<long> IdTileStack = Constants.Chat.ServerIdTileStack;
    private static readonly Dictionary<MediaId, Media.Media> EmptyMediaMap = new ();
    private static readonly ILookup<TextEntryId, TextEntryAttachment> EmptyAttachments
        = Array.Empty<TextEntryAttachment>().ToLookup(ta => ta.EntryId);
    private static readonly Task<ILookup<TextEntryId, TextEntryAttachment>> EmptyAttachmentsTask
        = Task.FromResult(EmptyAttachments);
    private static readonly IReadOnlyDictionary<Symbol, Media.LinkPreview> EmptyLinkPreviews
        = new Dictionary<Symbol, Media.LinkPreview>().AsReadOnly();
    private static readonly Task<IReadOnlyDictionary<Symbol, Media.LinkPreview>> EmptyLinkPreviewsTask
        = Task.FromResult(EmptyLinkPreviews);

    // all backend services should be requested lazily to avoid circular references!
    private IAccountsBackend? _accountsBackend;
    private IAuthorsBackend? _authorsBackend;
    private IRolesBackend? _rolesBackend;
    private IMediaBackend? _mediaBackend;
    private ILinkPreviewsBackend? _linkPreviewsBackend;
    private IAccountsBackend AccountsBackend => _accountsBackend ??= Services.GetRequiredService<IAccountsBackend>();
    private IAuthorsBackend AuthorsBackend => _authorsBackend ??= Services.GetRequiredService<IAuthorsBackend>();
    private IRolesBackend RolesBackend => _rolesBackend ??= Services.GetRequiredService<IRolesBackend>();
    private IMediaBackend MediaBackend => _mediaBackend ??= Services.GetRequiredService<IMediaBackend>();
    private ILinkPreviewsBackend LinkPreviewsBackend => _linkPreviewsBackend ??= Services.GetRequiredService<ILinkPreviewsBackend>();

    private KeyedFactory<IBackendChatMarkupHub, ChatId> ChatMarkupHubFactory { get; } = services.KeyedFactory<IBackendChatMarkupHub, ChatId>();
    private IDbEntityResolver<string, DbChat> DbChatResolver { get; } = services.GetRequiredService<IDbEntityResolver<string, DbChat>>();
    private IDbShardLocalIdGenerator<DbChatEntry, DbChatEntryShardRef> DbChatEntryIdGenerator { get; } = services.GetRequiredService<IDbShardLocalIdGenerator<DbChatEntry, DbChatEntryShardRef>>();
    private DiffEngine DiffEngine { get; } = services.GetRequiredService<DiffEngine>();
    private HostInfo HostInfo { get; } = services.HostInfo();
    private OtelMetrics Metrics { get; } = services.GetRequiredService<OtelMetrics>();

    // [ComputeMethod]
    public virtual async Task<Chat?> Get(ChatId chatId, CancellationToken cancellationToken)
    {
        if (chatId.IsNone)
            throw new ArgumentOutOfRangeException(nameof(chatId));

        var dbChat = await DbChatResolver.Get(chatId, cancellationToken).ConfigureAwait(false);
        var chat = dbChat?.ToModel();
        if (chat == null)
            return null;

        if (chat.MediaId.IsNone)
            return chat;

        var media = await MediaBackend.Get(chat.MediaId, cancellationToken).ConfigureAwait(false);
        return chat with { Picture = media };
    }

    // [ComputeMethod]
    public virtual async Task<Chat?> GetTemplatedChatFor(ChatId templateId, UserId userId, CancellationToken cancellationToken)
    {
        if (templateId.IsNone)
            throw new ArgumentOutOfRangeException(nameof(templateId));
        if (userId.IsNone)
            throw new ArgumentOutOfRangeException(nameof(userId));

        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);

        var dbChat = await dbContext.Chats
            .Where(c => c.TemplateId == templateId && c.TemplatedForUserId == userId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return dbChat?.ToModel();
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<ChatId>> GetPublicChatIdsFor(PlaceId placeId, CancellationToken cancellationToken)
    {
        if (placeId.IsNone)
            throw new ArgumentOutOfRangeException(nameof(placeId));

        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);

        var idPrefix = PlaceChatId.IdPrefix + placeId.Value;
        var sChatIds = await dbContext.Chats
            .Where(c => c.Id.StartsWith(idPrefix))
            .Where(c => c.IsPublic)
            .Select(c => c.Id)
            .OrderBy(c => c)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var placeChatIds = sChatIds
            .Select(c => new PlaceChatId(c))
            .Where(c => !c.IsRoot)
            .ToArray();
        return placeChatIds
            .ToApiArray(c => (ChatId)c);
    }

    // [ComputeMethod]
    public virtual async Task<AuthorRules> GetRules(
        ChatId chatId,
        PrincipalId principalId,
        CancellationToken cancellationToken)
    {
        if (chatId.IsPeerChat(out var peerChatId)) // We don't use actual roles to determine rules in this case
            return await GetPeerChatRules(peerChatId, principalId, cancellationToken).ConfigureAwait(false);

        // Group chat
        var chat = await Get(chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return AuthorRules.None(chatId);

        AuthorFull? author;
        AccountFull? account;
        if (principalId.IsUser(out var userId)) {
            account = await AccountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);
            if (account == null)
                return AuthorRules.None(chatId);

            author = await AuthorsBackend.GetByUserId(chatId, account.Id, cancellationToken).ConfigureAwait(false);
        }
        else if (principalId.IsAuthor(out var authorId)) {
            author = await AuthorsBackend.Get(chatId, authorId, cancellationToken).ConfigureAwait(false);
            if (author == null)
                return AuthorRules.None(chatId);

            account = await AccountsBackend.Get(author.UserId, cancellationToken).ConfigureAwait(false);
            if (account == null)
                return AuthorRules.None(chatId);
        }
        else
            return AuthorRules.None(chatId);

        var roles = ApiArray<Role>.Empty;
        var isJoined = author is { HasLeft: false };
        if (isJoined) {
            var isGuest = account.IsGuestOrNone;
            var isAnonymous = author is { IsAnonymous: true };
            roles = await RolesBackend
                .List(chatId, author!.Id, isGuest, isAnonymous, cancellationToken)
                .ConfigureAwait(false);
        }
        var permissions = roles.ToPermissions();
        if (chat.IsPublic) {
            if (chatId != Constants.Chat.AnnouncementsChatId)
                permissions |= ChatPermissions.Join;
            if (!isJoined) {
                var anyoneSystemRole = await RolesBackend.GetSystem(chatId, SystemRole.Anyone, cancellationToken).ConfigureAwait(false);
                if (anyoneSystemRole != null) {
                    // Full permissions of Anyone role are granted after you join,
                    // but until you joined, we grant only a subset of these permissions.
                    permissions |= anyoneSystemRole.Permissions & (ChatPermissions.Read | ChatPermissions.SeeMembers | ChatPermissions.Join);
                }
            }
        }
        permissions = permissions.AddImplied();

        var rules = new AuthorRules(chatId, author, account, permissions);
        return rules;
    }

    // [ComputeMethod]
    public virtual async Task<ChatNews> GetNews(
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var chat = await Get(chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return default;

        var idRange = await GetIdRange(chatId, ChatEntryKind.Text, false, cancellationToken).ConfigureAwait(false);
        var idTile = IdTileStack.FirstLayer.GetTile(idRange.End - 1);
        var tile = await GetTile(chatId, ChatEntryKind.Text, idTile.Range, false, cancellationToken).ConfigureAwait(false);
        var lastEntry = tile.Entries.Count > 0 ? tile.Entries[^1] : null;
        return new ChatNews(idRange, lastEntry);
    }

    // [ComputeMethod]
    public virtual async Task<long> GetEntryCount(
        ChatId chatId,
        ChatEntryKind entryKind,
        Range<long>? idTileRange,
        bool includeRemoved,
        CancellationToken cancellationToken)
    {
        var dbContext = CreateDbContext();
        await using var __ = dbContext.ConfigureAwait(false);

        var dbChatEntries = dbContext.ChatEntries
            .Where(e => e.ChatId == chatId && e.Kind == entryKind);
        if (!includeRemoved)
            dbChatEntries = dbChatEntries.Where(e => !e.IsRemoved);

        if (idTileRange.HasValue) {
            var idRangeValue = idTileRange.GetValueOrDefault();
            IdTileStack.AssertIsTile(idRangeValue);
            dbChatEntries = dbChatEntries
                .Where(e => e.LocalId >= idRangeValue.Start && e.LocalId < idRangeValue.End);
        }

        return await dbChatEntries.LongCountAsync(cancellationToken).ConfigureAwait(false);
    }

    // Note that it returns (firstId, lastId + 1) range!
    // [ComputeMethod]
    public virtual async Task<Range<long>> GetIdRange(
        ChatId chatId,
        ChatEntryKind entryKind,
        bool includeRemoved,
        CancellationToken cancellationToken)
    {
        var minId = await GetMinId(chatId, entryKind, cancellationToken).ConfigureAwait(false);

        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);

        var dbChatEntries = dbContext.ChatEntries
            .Where(e => e.ChatId == chatId && e.Kind == entryKind);
        if (!includeRemoved)
            dbChatEntries = dbChatEntries.Where(e => e.IsRemoved == false);
        var maxId = await dbChatEntries
            .OrderByDescending(e => e.LocalId)
            .Select(e => e.LocalId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return (minId, Math.Max(minId, maxId) + 1);
    }

    // [ComputeMethod]
    public virtual async Task<ChatTile> GetTile(
        ChatId chatId,
        ChatEntryKind entryKind,
        Range<long> idTileRange,
        bool includeRemoved,
        CancellationToken cancellationToken)
    {
        var idTile = IdTileStack.GetTile(idTileRange);
        var smallerIdTiles = idTile.Smaller();
        if (smallerIdTiles.Length != 0) {
            var smallerChatTiles = await smallerIdTiles
                .Select(sidTile => GetTile(chatId,
                    entryKind,
                    sidTile.Range,
                    includeRemoved,
                    cancellationToken))
                .Collect()
                .ConfigureAwait(false);
            return new ChatTile(smallerChatTiles, includeRemoved);
        }
        if (!includeRemoved) {
            var fullTile = await GetTile(chatId, entryKind, idTileRange, true, cancellationToken).ConfigureAwait(false);
            return new ChatTile(idTileRange, false, fullTile.Entries.Where(e => !e.IsRemoved).ToApiArray());
        }

        // If we're here, it's the smallest tile & includeRemoved = true
        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);

        var idRange = idTile.Range;
        var dbEntries = await dbContext.ChatEntries
            .Where(e => e.ChatId == chatId
                && e.Kind == entryKind
                && e.LocalId >= idRange.Start
                && e.LocalId < idRange.End)
            .OrderBy(e => e.LocalId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // audio or video entries doesn't have attachments now
        if (entryKind != ChatEntryKind.Text)
            return new ChatTile(
                idTileRange,
                true,
                dbEntries
                    .Select(dbe => dbe.ToModel())
                    .ToApiArray());

        var linkPreviewIds  = dbEntries.Where(x => !x.LinkPreviewId.IsNullOrEmpty())
            .Select(x => (Symbol)x.LinkPreviewId)
            .Distinct()
            .ToList();

        var allAttachmentsTask = GetAttachments();

        var allLinkPreviewsTask = linkPreviewIds.Count > 0
            ? GetLinkPreviews(linkPreviewIds)
            : EmptyLinkPreviewsTask;
        await Task.WhenAll(allAttachmentsTask, allLinkPreviewsTask).ConfigureAwait(false);

        var allAttachments = await allAttachmentsTask.ConfigureAwait(false);
        var allLinkPreviews = await allLinkPreviewsTask.ConfigureAwait(false);
        var entries = dbEntries.Select(e => {
            var entryId = TextEntryId.Parse(e.Id);
            var entryAttachments = allAttachments[entryId];
            var linkPreview = allLinkPreviews.GetValueOrDefault(e.LinkPreviewId);
            return e.ToModel(entryAttachments, linkPreview);
        });
        return new ChatTile(idTileRange, true, entries.ToApiArray());

        async Task<IReadOnlyDictionary<Symbol, Media.LinkPreview>> GetLinkPreviews(ICollection<Symbol> linkPreviewIds1)
        {
            if (linkPreviewIds1.Count == 0)
                return EmptyLinkPreviews;

            return (await linkPreviewIds1
                    .Select(id => LinkPreviewsBackend.Get(id, cancellationToken))
                    .Collect()
                    .ConfigureAwait(false))
                .Where(lp => lp != null)
                .ToDictionary(lp => lp!.Id)!;
        }

        Task<ILookup<TextEntryId, TextEntryAttachment>> GetAttachments()
        {
            var entryIdsWithAttachments = dbEntries.Where(x => x.HasAttachments)
                .Select(x => new TextEntryId(x.Id))
                .ToList();

            return entryIdsWithAttachments.Count > 0
                ? GetAttachmentsBulk()
                : EmptyAttachmentsTask;

            async Task<ILookup<TextEntryId,TextEntryAttachment>> GetAttachmentsBulk()
            {
                var attachments = await entryIdsWithAttachments.Select(x => GetEntryAttachments(x, cancellationToken)).Collect().ConfigureAwait(false);
                return attachments.SelectMany(x => x).ToLookup(x => x.EntryId);
            }
        }
    }

    [ComputeMethod]
    protected virtual async Task<ApiArray<TextEntryAttachment>> GetEntryAttachments(TextEntryId entryId, CancellationToken cancellationToken)
    {
        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);

        var idPrefix = DbTextEntryAttachment.IdPrefix(entryId);
        var dbAttachments = await dbContext.TextEntryAttachments
            .Where(x => x.Id.StartsWith(idPrefix))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var mediaIds = dbAttachments.Select(x => x.MediaId)
            .Concat(dbAttachments.Select(x => x.ThumbnailMediaId))
            .Select(MediaId.ParseOrNone)
            .Where(mid => !mid.IsNone)
            .ToList();
        var mediaMap = EmptyMediaMap;
        if (mediaIds.Count > 0) {
            var mediaList = await mediaIds
                .Select(mid => MediaBackend.Get(mid, cancellationToken))
                .Collect()
                .ConfigureAwait(false);
            mediaMap = mediaList
                .SkipNullItems()
                .DistinctBy(m => m.Id)
                .ToDictionary(m => m.Id);
        }
        return dbAttachments.Select(x => WithMedia(x.ToModel())).ToApiArray();

        TextEntryAttachment WithMedia(TextEntryAttachment attachment)
        {
            if (attachment.MediaId.IsNone)
                return attachment;

            var media = mediaMap.GetValueOrDefault(attachment.MediaId) ?? attachment.Media;
            var thumbnailMedia = attachment.ThumbnailMedia;
            if (!attachment.ThumbnailMediaId.IsNone)
                thumbnailMedia = mediaMap.GetValueOrDefault(attachment.ThumbnailMediaId) ?? thumbnailMedia;
            return attachment with {
                Media = media,
                ThumbnailMedia = thumbnailMedia,
            };
        }
    }

    // [CommandHandler]
    public virtual async Task<Chat> OnChange(
        ChatsBackend_Change command,
        CancellationToken cancellationToken)
    {
        var (chatId, expectedVersion, change, ownerId) = command;
        var context = CommandContext.GetCurrent();

        if (Computed.IsInvalidating()) {
            var invChat = context.Operation().Items.Get<Chat>();
            if (invChat != null) {
                _ = Get(invChat.Id, default);
                if (invChat is { TemplateId: not null, TemplatedForUserId: not null })
                    _ = GetTemplatedChatFor(invChat.TemplateId.Value, invChat.TemplatedForUserId.Value, default);
                if (invChat.Id.IsPlaceChat(out var placeChatId))
                    _ = GetPublicChatIdsFor(placeChatId.PlaceId, default);
            }
            return null!;
        }

        change.RequireValid();
        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbChat = chatId.IsNone ? null :
            await dbContext.Chats.ForUpdate()
                // ReSharper disable once AccessToModifiedClosure
                .FirstOrDefaultAsync(c => c.Id == chatId, cancellationToken)
                .ConfigureAwait(false);
        var oldChat = dbChat?.ToModel();
        Chat chat;
        if (change.IsCreate(out var update)) {
            oldChat.RequireNull();
            var placeId = update.PlaceId ?? PlaceId.None;
            var chatKind = update.Kind ?? (chatId.IsNone && !placeId.IsNone ? ChatKind.Place : chatId.Kind);
            if (chatKind == ChatKind.Group) {
                if (chatId.IsNone)
                    chatId = new ChatId(Generate.Option);
                else if (!Constants.Chat.SystemChatIds.Contains(chatId))
                    throw new ArgumentOutOfRangeException(nameof(command), "Invalid ChatId.");
            }
            else if (chatKind == ChatKind.Place) {
                if (chatId.IsNone) {
                    chatId = placeId.IsNone
                        ? PlaceChatId.GetForRoot(new PlaceId(Generate.Option))
                        : new PlaceChatId(placeId, Generate.Option);
                }
                else
                    throw new ArgumentOutOfRangeException(nameof(command), "Invalid ChatId.");
                update.ValidateForPlaceChat();
            }
            else if (chatKind != ChatKind.Peer)
                throw new ArgumentOutOfRangeException(nameof(command), "Invalid Change.Kind.");

            chat = new Chat(chatId) {
                CreatedAt = Clocks.SystemClock.Now,
            };
            chat = ApplyDiff(chat, update);
            dbChat = new DbChat(chat);
            if (!dbChat.SystemTag.IsNullOrEmpty()) {
                // Only group chats can have system tags
                ownerId.Require("Command.OwnerId");
                // Chats with system tags should be unique per user.
                var existingDbChat = await dbContext.Chats
                    .Join(dbContext.Authors, c => c.Id, a => a.ChatId, (c, a) => new { c, a })
                    .Where(x => x.a.UserId == ownerId && x.c.SystemTag == dbChat.SystemTag)
                    .Select(x => x.c)
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (existingDbChat != null)
                    return existingDbChat.ToModel();
            }

            dbContext.Add(dbChat);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            if (chatId.IsPeerChat(out var peerChatId)) {
                // Peer chat
                ownerId.RequireNone();

                // Creating authors
                await peerChatId.UserIds
                    .ToArray()
                    .Select(userId => AuthorsBackend.EnsureJoined(chatId, userId, cancellationToken))
                    .Collect()
                    .ConfigureAwait(false);
            }
            else if (chatId.Kind == ChatKind.Group || chatId.Kind == ChatKind.Place) {
                // Group chat
                ownerId.Require("Command.OwnerId");
                // If chat is created with possibility to join anonymous authors, then join owner as anonymous author.
                // After that they can reveal themself if needed.
                var upsertCommand = new AuthorsBackend_Upsert(
                    chatId, default, ownerId, null,
                    new AuthorDiff {
                        IsAnonymous = chat.AllowAnonymousAuthors
                    });
                var author = await Commander.Call(upsertCommand, true, cancellationToken).ConfigureAwait(false);

                if (chat.HasSingleAuthor) {
                    var createCustomRoleCmd = new RolesBackend_Change(chatId, default, null, new() {
                        Create = new RoleDiff {
                            Name = "SingleAuthor",
                            SystemRole = SystemRole.None,
                            Permissions = ChatPermissions.Write,
                            AuthorIds = new SetDiff<ApiArray<AuthorId>, AuthorId>() {
                                AddedItems = ApiArray<AuthorId>.Empty.Add(author.Id),
                            },
                        },
                    });
                    await Commander.Call(createCustomRoleCmd, cancellationToken).ConfigureAwait(false);
                }
                else {
                    var createOwnerRoleCmd = new RolesBackend_Change(chatId, default, null, new() {
                        Create = new RoleDiff {
                            SystemRole = SystemRole.Owner,
                            Permissions = ChatPermissions.Owner,
                            AuthorIds = new SetDiff<ApiArray<AuthorId>, AuthorId>() {
                                AddedItems = ApiArray<AuthorId>.Empty.Add(author.Id),
                            },
                        },
                    });
                    await Commander.Call(createOwnerRoleCmd, cancellationToken).ConfigureAwait(false);

                    var createAnyoneRoleCmd = new RolesBackend_Change(chatId,
                        default,
                        null,
                        new () {
                            Create = new RoleDiff() {
                                SystemRole = SystemRole.Anyone,
                                Permissions =
                                    ChatPermissions.Write
                                    | ChatPermissions.Invite
                                    | ChatPermissions.SeeMembers
                                    | ChatPermissions.Leave,
                            },
                        });
                    Log.LogInformation("Weird command: {Command}", createAnyoneRoleCmd);
                    await Commander.Call(createAnyoneRoleCmd, cancellationToken).ConfigureAwait(false);
                }
            }
            else
                throw new ArgumentOutOfRangeException(nameof(command), "Invalid ChatId.");
        }
        else if (change.IsUpdate(out update)) {
            ownerId.RequireNone();
            if (update.PlaceId.HasValue)
                throw new ArgumentOutOfRangeException(nameof(command), "ChatDiff.PlaceId should be null.");
            if (chatId.IsPlaceChat(out _))
                update.ValidateForPlaceChat();
            dbChat.RequireVersion(expectedVersion);

            chat = ApplyDiff(dbChat.ToModel(), update);
            dbChat.UpdateFrom(chat);
        }
        else if (change.IsRemove()) {
            dbChat.Require();

            if (!dbChat.MediaId.IsNullOrEmpty()) {
                var removeMediaCommand = new MediaBackend_Change(
                    new MediaId(dbChat.MediaId),
                    new Change<Media.Media> { Remove = true });
                await Commander.Call(removeMediaCommand, true, cancellationToken).ConfigureAwait(false);
            }
            var attachmentMediaIds = await dbContext.ChatEntries
                .Where(ce => ce.ChatId == chatId && ce.HasAttachments)
                .Join(dbContext.TextEntryAttachments, ce => ce.Id, ea => ea.EntryId, (_, ea) => ea.MediaId)
                .Distinct()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (var mediaId in attachmentMediaIds) {
                var removeMediaCommand = new MediaBackend_Change(
                    new MediaId(mediaId),
                    new Change<Media.Media> { Remove = true });
                await Commander.Call(removeMediaCommand, true, cancellationToken).ConfigureAwait(false);
            }
            // Remove attachments
            await dbContext.ChatEntries
                .Where(ce => ce.ChatId == chatId && ce.HasAttachments)
                .Join(dbContext.TextEntryAttachments, ce => ce.Id, ea => ea.EntryId, (_, ea) => ea)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
            // Remove reaction summaries
            await dbContext.ChatEntries
                .Where(ce => ce.ChatId == chatId)
                .Join(dbContext.ReactionSummaries, ce => ce.Id, rs => rs.EntryId, (_, rs) => rs)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
            // Remove reactions
            await dbContext.ChatEntries
                .Where(ce => ce.ChatId == chatId)
                .Join(dbContext.Reactions, ce => ce.Id, rs => rs.EntryId, (_, rs) => rs)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
            // Remove mentions
            await dbContext.ChatEntries
                .Where(ce => ce.ChatId == chatId)
                .Join(dbContext.Mentions.Where(m => m.ChatId == chatId), ce => ce.LocalId, rs => rs.EntryId, (_, rs) => rs)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
            // Remove entries
            await dbContext.ChatEntries
                .Where(ce => ce.ChatId == chatId)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
            // Remove roles
            await dbContext.Roles
                .Where(r => r.ChatId == chatId)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
            // Remove authors
            var removeAuthorsCommand = new AuthorsBackend_Remove(chatId, AuthorId.None, UserId.None);
            await Commander.Call(removeAuthorsCommand, false, cancellationToken).ConfigureAwait(false);
            dbContext.Remove(dbChat);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        chat = dbChat.Require().ToModel();
        context.Operation().Items.Set(chat);

        // Raise events
        new ChatChangedEvent(chat, oldChat, change.Kind)
            .EnqueueOnCompletion();
        return chat;

        Chat ApplyDiff(Chat originalChat, ChatDiff? diff) {
            // Update
            var newChat = DiffEngine.Patch(originalChat, diff) with {
                Version = VersionGenerator.NextVersion(originalChat.Version),
            };
            if (newChat.Kind != originalChat.Kind)
                throw StandardError.Constraint("Chat kind cannot be changed.");

            // Validation
            switch (newChat.Kind) {
            case ChatKind.Group:
                if (newChat.Title.IsNullOrEmpty())
                    throw StandardError.Constraint("Chat title cannot be empty.");
                break;
            case ChatKind.Peer:
                if (!newChat.Title.IsNullOrEmpty())
                    throw StandardError.Constraint("Peer chat title must be empty.");
                break;
            case ChatKind.Place:
                if (newChat.Title.IsNullOrEmpty())
                    throw StandardError.Constraint("Place chat title must be empty.");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(command), "Invalid chat kind.");
            }
            return newChat;
        }
    }

    // [CommandHandler]
    public virtual async Task<ChatEntry> OnUpsertEntry(
        ChatsBackend_UpsertEntry command,
        CancellationToken cancellationToken)
    {
        var entry = command.Entry;
        var changeKind = entry.LocalId == 0
            ? ChangeKind.Create
            : entry.IsRemoved ? ChangeKind.Remove : ChangeKind.Update;
        var chatId = entry.ChatId;
        var context = CommandContext.GetCurrent();

        if (Computed.IsInvalidating()) {
            var invChatEntry = context.Operation().Items.Get<ChatEntry>();
            if (invChatEntry != null)
                InvalidateTiles(chatId, entry.Kind, invChatEntry.LocalId, changeKind);

            // Invalidate min-max Id range at last
            switch (changeKind) {
            case ChangeKind.Create:
                _ = GetIdRange(chatId, entry.Kind, true, default);
                _ = GetIdRange(chatId, entry.Kind, false, default);
                break;
            case ChangeKind.Remove:
                _ = GetIdRange(chatId, entry.Kind, false, default);
                break;
            }
            return null!;
        }

        if (HostInfo.IsDevelopmentInstance && entry.Kind == ChatEntryKind.Text && OrdinalEquals(entry.Content, "<error>"))
            throw StandardError.Internal("Just a test error.");

        if (chatId.IsPeerChat(out var peerChatId))
            _ = await EnsureExists(peerChatId, cancellationToken).ConfigureAwait(false);

        // Injecting mention names into the markup
        var chatMarkupHub = ChatMarkupHubFactory[chatId];
        entry = await chatMarkupHub.PrepareForSave(entry, cancellationToken).ConfigureAwait(false);

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbEntry = await DbUpsertEntry(dbContext, entry, command.HasAttachments, cancellationToken).ConfigureAwait(false);
        entry = dbEntry.ToModel();
        context.Operation().Items.Set(entry);

        if (entry.Kind != ChatEntryKind.Text)
            return entry;
        if (changeKind == ChangeKind.Remove || entry.IsStreaming)
            return entry;

        if (changeKind == ChangeKind.Create)
            Metrics.MessageCount.Add(1);

        // Let's enqueue the TextEntryChangedEvent
        var authorId = entry.AuthorId;
        var author = await AuthorsBackend.Get(chatId, authorId, cancellationToken).ConfigureAwait(false);
        // Raise events
        new TextEntryChangedEvent(entry, author!, changeKind)
            .EnqueueOnCompletion();
        return entry;
    }

    // [CommandHandler]
    public virtual async Task<ApiArray<TextEntryAttachment>> OnCreateAttachments(
        ChatsBackend_CreateAttachments command,
        CancellationToken cancellationToken)
    {
        var attachments = command.Attachments;
        if (attachments.Count > Constants.Attachments.FileCountLimit)
            throw StandardError.Constraint("Too many attachments in bulk.");

        var entryIds = attachments.Select(x => x.EntryId).Distinct().ToList();
        if (entryIds.Count > 1)
            throw StandardError.Constraint("Attachments cannot belong to different messages.");

        var entryId = entryIds[0];

        if (Computed.IsInvalidating()) {
            _ = GetEntryAttachments(entryId, default);
            InvalidateTiles(entryId.ChatId, ChatEntryKind.Text, entryId.LocalId, ChangeKind.Update);
            return default!;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbAttachments = new List<DbTextEntryAttachment>();
        foreach (var attachment in attachments) {

            var dbChatEntry = await dbContext.ChatEntries.Get(entryId, cancellationToken)
                .Require()
                .ConfigureAwait(false);
            if (dbChatEntry.IsRemoved)
                throw StandardError.Constraint("Removed chat entries cannot be modified.");

            var dbAttachment = new DbTextEntryAttachment(attachment with {
                Version = VersionGenerator.NextVersion(),
            });
            dbContext.Add(dbAttachment);
            dbAttachments.Add(dbAttachment);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return dbAttachments.Select(x => x.ToModel()).ToApiArray();
    }

    // [CommandHandler]
    public virtual async Task OnRemoveOwnChats(
        ChatsBackend_RemoveOwnChats command,
        CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var userId = command.UserId;
        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var chatIdsToDelete = new List<string>();
        var ownChatIds = await dbContext.Chats
            .Join(dbContext.Roles, c => c.Id, r => r.ChatId, (c, r) => new { c, r })
            .Join(dbContext.AuthorRoles, x => x.r.Id, r => r.DbRoleId, (x, r) => new { x.c, x.r, ar = r })
            .Join(dbContext.Authors, x => x.ar.DbAuthorId, a => a.Id, (x, a) => new { x.c, x.r, x.ar, a })
            .Where(x => x.a.UserId == userId && x.r.SystemRole == SystemRole.Owner)
            .Select(x => x.c.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var chatId in ownChatIds) {
            var hasOtherOwners = await dbContext.Chats
                .Join(dbContext.Roles, c => c.Id, r => r.ChatId, (c, r) => new { c, r })
                .Join(dbContext.AuthorRoles, x => x.r.Id, r => r.DbRoleId, (x, r) => new { x.c, x.r, ar = r })
                .Join(dbContext.Authors, x => x.ar.DbAuthorId, a => a.Id, (x, a) => new { x.c, x.r, x.ar, a })
                .Where(x => x.c.Id == chatId && x.a.UserId != userId && x.r.SystemRole == SystemRole.Owner)
                .AnyAsync(cancellationToken)
                .ConfigureAwait(false);

            if (!hasOtherOwners)
                chatIdsToDelete.Add(chatId);
        }
        foreach (var chatId in chatIdsToDelete) {
            var deleteChatCommand = new ChatsBackend_Change(
                new ChatId(chatId),
                null,
                new Change<ChatDiff> { Remove = true });

            await Commander.Call(deleteChatCommand, cancellationToken).ConfigureAwait(false);
        }
    }

    // [CommandHandler]
    public virtual async Task OnRemoveOwnEntries(
        ChatsBackend_RemoveOwnEntries command,
        CancellationToken cancellationToken)
    {
        var context = CommandContext.GetCurrent();

        if (Computed.IsInvalidating()) {
            var invChats = context.Operation().Items.Get<Dictionary<string,long>>();
            if (invChats == null)
                return;

            var tileSize = Constants.Chat.ServerIdTileStack.MinTileSize;
            foreach (var chatEntryPair in invChats) {
                var chatId = new ChatId(chatEntryPair.Key);
                var entryId = chatEntryPair.Value;
                InvalidateTiles(chatId, ChatEntryKind.Text, entryId, ChangeKind.Remove);
                InvalidateTiles(chatId, ChatEntryKind.Text, entryId - tileSize, ChangeKind.Remove);
                InvalidateTiles(chatId, ChatEntryKind.Text, entryId - tileSize*2, ChangeKind.Remove);
                InvalidateTiles(chatId, ChatEntryKind.Text, entryId - tileSize*3, ChangeKind.Remove);
                InvalidateTiles(chatId, ChatEntryKind.Text, entryId - tileSize*4, ChangeKind.Remove);
                _ = GetEntryAttachments(new TextEntryId(chatId, entryId, AssumeValid.Option), default);
            }
            return;
        }

        var chatEntriesToInvalidate = new Dictionary<string, long>(StringComparer.Ordinal);
        var userId = command.UserId;
        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var chatAuthors = await dbContext.Authors
            .Where(a => a.UserId == userId)
            .Select(a => new { a.ChatId, a.Id })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var chatAuthor in chatAuthors) {
            var chatId = chatAuthor.ChatId;
            var authorId = chatAuthor.Id;
            var attachmentMediaIds = await dbContext.ChatEntries
                .Where(ce => ce.ChatId == chatId && ce.AuthorId == authorId && ce.HasAttachments)
                .Join(dbContext.TextEntryAttachments, ce => ce.Id, ea => ea.EntryId, (_, ea) => ea.MediaId)
                .Distinct()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (var mediaId in attachmentMediaIds) {
                var removeMediaCommand = new MediaBackend_Change(
                    new MediaId(mediaId),
                    new Change<Media.Media> { Remove = true });
                await Commander.Call(removeMediaCommand, true, cancellationToken).ConfigureAwait(false);
            }

            // Remove attachments
            await dbContext.ChatEntries
                .Where(ce => ce.ChatId == chatId && ce.AuthorId == authorId && ce.HasAttachments)
                .Join(dbContext.TextEntryAttachments, ce => ce.Id, ea => ea.EntryId, (_, ea) => ea)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
            // Remove reaction summaries
            await dbContext.ChatEntries
                .Where(ce => ce.ChatId == chatId && ce.AuthorId == authorId)
                .Join(dbContext.ReactionSummaries, ce => ce.Id, rs => rs.EntryId, (_, rs) => rs)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
            // Remove reactions
            await dbContext.ChatEntries
                .Where(ce => ce.ChatId == chatId && ce.AuthorId == authorId)
                .Join(dbContext.Reactions, ce => ce.Id, rs => rs.EntryId, (_, rs) => rs)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
            // Remove mentions
            await dbContext.ChatEntries
                .Where(ce => ce.ChatId == chatId && ce.AuthorId == authorId)
                .Join(dbContext.Mentions.Where(m => m.ChatId == chatId), ce => ce.LocalId, rs => rs.EntryId, (_, rs) => rs)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            var lastAuthorEntryId = await dbContext.ChatEntries
                .Where(ce => ce.ChatId == chatId && ce.AuthorId == authorId)
                .OrderByDescending(ce => ce.LocalId)
                .Select(ce => ce.LocalId)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            chatEntriesToInvalidate.Add(chatId, lastAuthorEntryId);

            // Remove entries
            await dbContext.ChatEntries
                .Where(ce => ce.ChatId == chatId && ce.AuthorId == authorId)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        context.Operation().Items.Set(chatEntriesToInvalidate);
    }

    public virtual async Task OnCreateNotesChat(ChatsBackend_CreateNotesChat command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var userId = command.UserId;
        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var hasNotesChat = await dbContext.Chats
            .Join(dbContext.Authors, c => c.Id, a => a.ChatId, (c, a) => new { c, a })
            .AnyAsync(x => x.a.UserId == userId && x.c.SystemTag == Constants.Chat.SystemTags.Notes.Value, cancellationToken)
            .ConfigureAwait(false);

        if (hasNotesChat)
            return;

        var createNotesCommand = new ChatsBackend_Change(
            ChatId.None,
            null,
            new Change<ChatDiff> {
                Create = new ChatDiff {
                    Title = "Notes",
                    Kind = ChatKind.Group,
                    IsPublic = false,
                    MediaId = new MediaId("system-icons:notes"),
                    IsTemplate = false,
                    AllowGuestAuthors = false,
                    AllowAnonymousAuthors = false,
                    SystemTag = Constants.Chat.SystemTags.Notes,
                },
            },
            userId);
        await Commander.Call(createNotesCommand, cancellationToken).ConfigureAwait(false);
    }

    // Event handlers

    [EventHandler]
    public virtual async Task OnNewUserEvent(NewUserEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        await JoinAnnouncementsChat(eventCommand.UserId, cancellationToken).ConfigureAwait(false);
        await CreateNotesChat(eventCommand.UserId, cancellationToken).ConfigureAwait(false);

        if (HostInfo.IsDevelopmentInstance) {
            await JoinDefaultChatIfAdmin(eventCommand.UserId, cancellationToken).ConfigureAwait(false);
            await JoinFeedbackTemplateChatIfAdmin(eventCommand.UserId, cancellationToken).ConfigureAwait(false);
        }
    }

    [EventHandler]
    public virtual async Task OnAuthorChangedEvent(AuthorChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var (author, oldAuthor) = eventCommand;
        if (author.ChatId == Constants.Chat.AnnouncementsChatId || author.ChatId.IsPeerChat(out _))
            return;

        var oldHasLeft = oldAuthor?.HasLeft ?? true;
        if (oldHasLeft == author.HasLeft)
            return;

        // Skip for system admin user
        if (author.UserId == Constants.User.Admin.UserId)
            return;

        // Skip for template chats
        var chat = await Get(author.ChatId, cancellationToken).ConfigureAwait(false);
        if (chat is { IsTemplate: true })
            return;

        // and template chat owners
        var ownerRole = await RolesBackend.GetSystem(author.ChatId, SystemRole.Owner, cancellationToken).ConfigureAwait(false);
        if (chat is { TemplatedForUserId: not null } && ownerRole != null && author.RoleIds.Contains(ownerRole.Id))
            return;

        // and chats with predefined tags
        if (chat is { SystemTag.IsEmpty: false })
            return;

        // Let's delay fetching an author a bit
        Author? readAuthor = null;
        var retrier = new Retrier(5, RetryDelaySeq.Exp(0.25, 1));
        while (retrier.NextOrThrow()) {
            await Clocks.CoarseCpuClock.Delay(retrier.Delay, cancellationToken).ConfigureAwait(false);
            readAuthor = await AuthorsBackend.Get(author.ChatId, author.Id, cancellationToken).ConfigureAwait(false);
            if (readAuthor?.Avatar != null)
                break;
        }
        var authorId = author.Id;
        string authorName = "";
        if (readAuthor != null) {
            if (readAuthor.IsAnonymous)
                authorId = AuthorId.None;
            authorName = readAuthor.IsAnonymous ? "Someone" : readAuthor.Avatar.Name;
        }
        if (authorName.IsNullOrEmpty())
            authorName = MentionMarkup.NotAvailableName;

        var entryId = new ChatEntryId(author.ChatId, ChatEntryKind.Text, 0, AssumeValid.Option);
        var command = new ChatsBackend_UpsertEntry(new ChatEntry(entryId) {
            AuthorId = Bots.GetWalleId(author.ChatId),
            SystemEntry = new MembersChangedOption(authorId, authorName, author.HasLeft),
        });
        await Commander.Call(command, true, cancellationToken).ConfigureAwait(false);
    }

    // Protected methods

    [ComputeMethod]
    protected virtual async Task<long> GetMinId(
        ChatId chatId,
        ChatEntryKind entryKind,
        CancellationToken cancellationToken)
    {
        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);

        return await dbContext.ChatEntries
            .Where(e => e.ChatId == chatId && e.Kind == entryKind)
            .OrderBy(e => e.LocalId)
            .Select(e => e.LocalId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    protected void InvalidateTiles(ChatId chatId, ChatEntryKind entryKind, long entryId, ChangeKind changeKind)
    {
        // Invalidate global entry counts
        switch (changeKind) {
        case ChangeKind.Create:
            _ = GetEntryCount(chatId, entryKind, null, false, default);
            _ = GetEntryCount(chatId, entryKind, null, true, default);
            break;
        case ChangeKind.Remove:
            _ = GetEntryCount(chatId, entryKind, null, false, default);
            break;
        }

        // Invalidate GetTile & GetEntryCount for chat tiles
        foreach (var idTile in IdTileStack.GetAllTiles(entryId)) {
            if (idTile.Layer.Smaller == null) {
                // Larger tiles are composed out of smaller tiles,
                // so we have to invalidate just the smallest one.
                // And the tile with includeRemoved == false is based on
                // a tile with includeRemoved == true, so we have to invalidate
                // just this tile.
                _ = GetTile(chatId, entryKind, idTile.Range, true, default);
            }
            switch (changeKind) {
            case ChangeKind.Create:
                _ = GetEntryCount(chatId, entryKind, idTile.Range, true, default);
                _ = GetEntryCount(chatId, entryKind, idTile.Range, false, default);
                break;
            case ChangeKind.Remove:
                _ = GetEntryCount(chatId, entryKind, idTile.Range, false, default);
                break;
            }
        }
    }

    protected async Task<DbChatEntry> DbUpsertEntry(
        ChatDbContext dbContext,
        ChatEntry entry,
        bool hasAttachments,
        CancellationToken cancellationToken)
    {
        // AK: Suspicious - probably can lead to performance issues
        // AY: Yes, but the goal is to have a dense sequence here;
        //     later we'll change this to something that's more performant.
        var entryId = entry.Id;
        var chatId = entry.ChatId;
        var kind = entry.Kind;
        var isNew = entryId.LocalId == 0;

        DbChatEntry dbEntry;
        if (isNew) {
            var localId = await DbNextLocalId(dbContext, entry.ChatId, kind, cancellationToken).ConfigureAwait(false);
            entryId = new ChatEntryId(chatId, kind, localId, AssumeValid.Option);
            entry = entry with {
                Id = entryId,
                Version = VersionGenerator.NextVersion(),
                BeginsAt = Clocks.SystemClock.Now,
            };
            dbEntry = new (entry) {
                HasAttachments = hasAttachments,
            };

            dbContext.Add(dbEntry);
        }
        else {
            dbEntry = await dbContext.ChatEntries
                .Get(entryId, cancellationToken)
                .RequireVersion(entry.Version)
                .ConfigureAwait(false)
                ?? throw StandardError.NotFound<ChatEntry>();
            if (dbEntry.IsRemoved && entry.IsRemoved)
                throw StandardError.Constraint("Removed chat entries cannot be modified.");
            entry = entry with {
                Version = VersionGenerator.NextVersion(dbEntry.Version),
            };
            dbEntry.UpdateFrom(entry);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return dbEntry;
    }

    // Private / internal methods

    private async Task JoinAnnouncementsChat(UserId userId, CancellationToken cancellationToken)
    {
        var chatId = Constants.Chat.AnnouncementsChatId;
        var author = await AuthorsBackend.EnsureJoined(chatId, userId, cancellationToken).ConfigureAwait(false);

        if (!HostInfo.IsDevelopmentInstance)
            return;

        var account = await AccountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);
        if (account is not { IsAdmin: true })
            return;

        await AddOwner(chatId, author, cancellationToken).ConfigureAwait(false);
    }

    private async Task CreateNotesChat(UserId userId, CancellationToken cancellationToken)
    {
        var createNotesCommand = new ChatsBackend_CreateNotesChat(userId);
        await Commander.Run(createNotesCommand, true, cancellationToken).ConfigureAwait(false);
    }

    private async Task JoinDefaultChatIfAdmin(UserId userId, CancellationToken cancellationToken)
    {
        var account = await AccountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);
        if (account is not { IsAdmin: true })
            return;

        var chatId = Constants.Chat.DefaultChatId;
        var author = await AuthorsBackend.EnsureJoined(chatId, userId, cancellationToken).ConfigureAwait(false);

        await AddOwner(chatId, author, cancellationToken).ConfigureAwait(false);
    }

    private async Task JoinFeedbackTemplateChatIfAdmin(UserId userId, CancellationToken cancellationToken)
    {
        var account = await AccountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);
        if (account is not { IsAdmin: true })
            return;

        var email = account.GetVerifiedEmail();
        if (email.IsNullOrEmpty())
            return;
        if (!email.OrdinalEndsWith(Constants.Team.EmailSuffix))
            return;

        var chatId = Constants.Chat.FeedbackTemplateChatId;
        var author = await AuthorsBackend.EnsureJoined(chatId, userId, cancellationToken).ConfigureAwait(false);

        await AddOwner(chatId, author, cancellationToken).ConfigureAwait(false);
    }

    private async Task AddOwner(ChatId chatId, Author author, CancellationToken cancellationToken)
    {
        var ownerRole = await RolesBackend.GetSystem(chatId, SystemRole.Owner, cancellationToken)
            .Require()
            .ConfigureAwait(false);

        var changeCommand = new RolesBackend_Change(chatId,
            ownerRole.Id,
            null,
            new Change<RoleDiff> {
                Update = new RoleDiff {
                    AuthorIds = new SetDiff<ApiArray<AuthorId>, AuthorId> {
                        AddedItems = ApiArray<AuthorId>.Empty.Add(author.Id),
                    },
                },
            });
        await Commander.Call(changeCommand, cancellationToken).ConfigureAwait(false);
    }

    internal Task<long> DbNextLocalId(
        ChatDbContext dbContext,
        ChatId chatId,
        ChatEntryKind entryKind,
        CancellationToken cancellationToken)
        => DbChatEntryIdGenerator.Next(dbContext, new DbChatEntryShardRef(chatId, entryKind), cancellationToken);

    private async Task<AuthorRules> GetPeerChatRules(
        PeerChatId chatId,
        PrincipalId principalId,
        CancellationToken cancellationToken)
    {
        AuthorFull? author = null;
        AccountFull? account = null;
        if (principalId.IsUser(out var userId))
            account = await AccountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);
        else if (principalId.IsAuthor(out var authorId)) {
            author = await AuthorsBackend.Get(chatId, authorId, cancellationToken).ConfigureAwait(false);
            if (author == null)
                return AuthorRules.None(chatId);

            account = await AccountsBackend.Get(author.UserId, cancellationToken).ConfigureAwait(false);
        }
        if (account == null)
            return AuthorRules.None(chatId);

        var otherUserId = chatId.UserIds.OtherThanOrDefault(account.Id);
        if (otherUserId.IsGuestOrNone)
            return AuthorRules.None(chatId);

        if (account.IsGuestOrNone) {
            // We grant guest a permission to "read" the chat (which is going to be empty anyway)
            // solely to make sure ChatPage can display it like it already exists.
            // The footer there should contain "Sign in to chat" button in this case.
            // Once this guest signs in, he'll be redirected to actual peer with otherUserId.
            return new(chatId, author, account, (ChatPermissions.SeeMembers | ChatPermissions.Join).AddImplied());
        }

        var permissions = (ChatPermissions.Write | ChatPermissions.SeeMembers | ChatPermissions.Join).AddImplied();
        return new(chatId, author, account, permissions);
    }

    private async Task<Chat> EnsureExists(PeerChatId peerChatId, CancellationToken cancellationToken)
    {
        if (peerChatId.IsNone)
            throw new ArgumentOutOfRangeException(nameof(peerChatId));

        var chat = await Get(peerChatId, cancellationToken).ConfigureAwait(false);
        if (chat.IsStored())
            return chat;

        var command = new ChatsBackend_Change(peerChatId, null, new() { Create = new ChatDiff() });
        chat = await Commander.Call(command, true, cancellationToken).ConfigureAwait(false);
        return chat;
    }
}
