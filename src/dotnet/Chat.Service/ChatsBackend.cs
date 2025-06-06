using ActualChat.Chat.Db;
using ActualChat.Chat.Flows;
using ActualChat.Chat.Module;
using ActualChat.Db;
using ActualChat.Diagnostics;
using ActualChat.Flows;
using ActualChat.Hosting;
using ActualChat.Invite;
using ActualChat.Kvas;
using ActualChat.Media;
using ActualChat.Users;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;
using RangeExt = ActualChat.Mathematics.RangeExt;

namespace ActualChat.Chat;

public partial class ChatsBackend(IServiceProvider services) : DbServiceBase<ChatDbContext>(services), IChatsBackend
{
    private const string CreatedChatEntryId = "CreatedChatEntryId";
    private static readonly TileStack<long> IdTileStack = Constants.Chat.ServerIdTileStack;
    private static readonly Dictionary<MediaId, Media.Media> EmptyMediaMap = new ();
    private static readonly ILookup<TextEntryId, TextEntryAttachment> EmptyAttachments
        = Array.Empty<TextEntryAttachment>().ToLookup(ta => ta.EntryId);
    private static readonly Task<ILookup<TextEntryId, TextEntryAttachment>> EmptyAttachmentsTask
        = Task.FromResult(EmptyAttachments);
    private static readonly IReadOnlyDictionary<Symbol, LinkPreview> EmptyLinkPreviews
        = new Dictionary<Symbol, LinkPreview>().AsReadOnly();
    private static readonly Task<IReadOnlyDictionary<Symbol, LinkPreview>> EmptyLinkPreviewsTask
        = Task.FromResult(EmptyLinkPreviews);

    // all backend services should be requested lazily to avoid circular references!

    [field: AllowNull, MaybeNull]
    private IAccountsBackend AccountsBackend => field ??= Services.GetRequiredService<IAccountsBackend>();
    [field: AllowNull, MaybeNull]
    private IAuthorsBackend AuthorsBackend => field ??= Services.GetRequiredService<IAuthorsBackend>();
    [field: AllowNull, MaybeNull]
    private IRolesBackend RolesBackend => field ??= Services.GetRequiredService<IRolesBackend>();
    [field: AllowNull, MaybeNull]
    private IMediaBackend MediaBackend => field ??= Services.GetRequiredService<IMediaBackend>();
    [field: AllowNull, MaybeNull]
    private ILinkPreviewsBackend LinkPreviewsBackend => field ??= Services.GetRequiredService<ILinkPreviewsBackend>();
    [field: AllowNull, MaybeNull]
    private IInvitesBackend InvitesBackend => field ??= Services.GetRequiredService<IInvitesBackend>();
    [field: AllowNull, MaybeNull]
    private IPlacesBackend PlacesBackend => field ??= Services.GetRequiredService<IPlacesBackend>();
    [field: AllowNull, MaybeNull]
    private IConversationsBackend ConversationsBackend => field ??= Services.GetRequiredService<IConversationsBackend>();
    [field: AllowNull, MaybeNull]
    private IRouletteBackend RouletteBackend => field ??= Services.GetRequiredService<IRouletteBackend>();
    [field: AllowNull, MaybeNull]
    private IServerKvasBackend ServerKvasBackend => field ??= Services.GetRequiredService<IServerKvasBackend>();
    [field: AllowNull, MaybeNull]
    private HostInfo HostInfo => field ??= Services.HostInfo();
    [field: AllowNull, MaybeNull]
    private IMarkupParser MarkupParser => field ??= Services.GetRequiredService<IMarkupParser>();
    [field: AllowNull, MaybeNull]
    private KeyedFactory<IBackendChatMarkupHub, ChatId> ChatMarkupHubFactory => field ??= Services.KeyedFactory<IBackendChatMarkupHub, ChatId>();
    [field: AllowNull, MaybeNull]
    private IDbEntityResolver<string, DbChat> DbChatResolver => field ??= Services.GetRequiredService<IDbEntityResolver<string, DbChat>>();
    [field: AllowNull, MaybeNull]
    private IDbEntityResolver<string, DbChatCopyState> DbChatCopyStateResolver => field ??= Services.GetRequiredService<IDbEntityResolver<string, DbChatCopyState>>();
    [field: AllowNull, MaybeNull]
    private IDbEntityResolver<string, DbReadPositionsStat> DbReadPositionsStatResolver => field ??= Services.GetRequiredService<IDbEntityResolver<string, DbReadPositionsStat>>();
    [field: AllowNull, MaybeNull]
    private IDbShardLocalIdGenerator<DbChatEntry, DbChatEntryShardRef> DbChatEntryIdGenerator => field ??= Services.GetRequiredService<IDbShardLocalIdGenerator<DbChatEntry, DbChatEntryShardRef>>();
    [field: AllowNull, MaybeNull]
    private DiffEngine DiffEngine => field ??= Services.GetRequiredService<DiffEngine>();
    [field: AllowNull, MaybeNull]
    private IFlows Flows => field ??= Services.GetRequiredService<IFlows>();
    [field: AllowNull, MaybeNull]
    private ChatSettings Settings => field ??= Services.GetRequiredService<ChatSettings>();

    // [ComputeMethod]
    public virtual async Task<Chat?> Get(ChatId chatId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(chatId);

        var dbChat = await DbChatResolver.Get(chatId.Value, cancellationToken).ConfigureAwait(false);
        var chat = dbChat?.ToModel();
        if (chat == null)
            return null;

        if (chat.MediaId == null)
            return chat;

        var media = await MediaBackend.Get(chat.MediaId, cancellationToken).ConfigureAwait(false);
        return chat with { Picture = media };
    }

    [ComputeMethod]
    protected virtual Task<Unit> PseudoList()
        => ActualLab.Async.TaskExt.UnitTask;

    // [ComputeMethod]
    public virtual async Task<Chat?> GetTemplatedChatFor(ChatId templateId, UserId userId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(templateId);
        ArgumentNullException.ThrowIfNull(userId);

        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var dbChat = await dbContext.Chats
            .Where(c => c.TemplateId == templateId.Value && c.TemplatedForUserId == userId.Value)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return dbChat?.ToModel();
    }

    // [ComputeMethod]
    public virtual async Task<long?> GetMaxEntryVersion(ChatId chatId, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var sid = chatId.Value;
        return await dbContext.ChatEntries.Where(x => x.Id == sid && x.Kind == ChatEntryKind.Text)
            .MaxAsync(x => (long?)x.Version, cancellationToken)
            .ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ChatId[]> GetPublicChatIdsFor(PlaceId? placeId, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var idPrefix = PlaceChatId.IdPrefix + (placeId?.Value ?? "");
        var sChatIds = await dbContext.Chats
#pragma warning disable MA0074
            .WhereIf(c => c.Id.StartsWith(idPrefix), placeId is not null) // place chats
            .WhereIf(c => !c.Id.StartsWith(idPrefix), placeId is null) // non-place chats
#pragma warning restore MA0074
            .Where(c => c.IsPublic)
            .Select(c => c.Id)
            .OrderBy(c => c)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return sChatIds
            .Select(ChatId.Parse)
            .Where(id => id is not PlaceChatId { IsRoot: true })
            .ToArray();
    }

    // [ComputeMethod]
    public virtual async Task<PlaceChatId[]> ListPlaceChatIds(PlaceId placeId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(placeId);

        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var idPrefix = PlaceChatId.IdPrefix + placeId.Value;
        var sChatIds = await dbContext.Chats
            .Where(c => c.Id.StartsWith(idPrefix))
            .Select(c => c.Id)
            .OrderBy(c => c)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return sChatIds.Select(x => (PlaceChatId)ChatId.Parse(x)).Where(x => !x.IsRoot).ToArray();
    }

    // [ComputeMethod]
    public virtual async Task<AuthorRules> GetRules(
        ChatId chatId,
        PrincipalId principalId,
        CancellationToken cancellationToken)
    {
        if (chatId is PeerChatId peerChatId) // We don't use actual roles to determine rules in this case
            return await GetPeerChatRules(peerChatId, principalId, cancellationToken).ConfigureAwait(false);

        if (chatId.IsThread(out var threadChatId)) {
            var parentChatId = threadChatId.GetOutermostParent();
            var parentChatPrincipal = ActualChat.Chat.AuthorsBackend.Remap(principalId, parentChatId);
            var parentChatRules = await GetRules(parentChatId, parentChatPrincipal, cancellationToken).ConfigureAwait(false);
            if (!parentChatRules.CanRead())
                return AuthorRules.None(chatId);

            var account = parentChatRules.Account;
            var threadChatAuthor = await AuthorsBackend.GetByUserId(chatId, account.Require().Id, RequestedAuthorKind.Full, cancellationToken).ConfigureAwait(false);
            var threadPermissions = ChatPermissions.Read;
            if (parentChatRules.CanWrite() && threadChatAuthor is not null)
                threadPermissions |= ChatPermissions.Write;
            return new AuthorRules(chatId, threadChatAuthor, account, threadPermissions);
        }

        AuthorRules chatRules;
        if (chatId is PlaceChatId { IsRoot: false } placeChatId)
            chatRules = await GetPlaceChatRules(placeChatId, principalId, cancellationToken).ConfigureAwait(false);
        else
            // Group chat or Root place chat
            chatRules = await GetRulesRaw(chatId, principalId, cancellationToken).ConfigureAwait(false);

        if (chatRules.Permissions != default) {
            var chat = await Get(chatId, cancellationToken).ConfigureAwait(false);
            if (chat == null)
                return AuthorRules.None(chatId);

            if (chat.IsArchived) {
                if (!chatRules.IsOwner())
                    return AuthorRules.None(chatId);

                var permissionsToExclude = ChatPermissions.Write
                    | ChatPermissions.Join
                    | ChatPermissions.Invite
                    | ChatPermissions.EditMembers;
                return chatRules with {
                    Permissions = chatRules.Permissions & ~permissionsToExclude // Do not allow to write/join on archived chat
                };
            }
        }
        return chatRules;
    }

    // [ComputeMethod]
    public virtual async Task<ChatNews?> GetNews(
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var chat = await Get(chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return null;

        var idRange = await GetIdRange(chatId, ChatEntryKind.Text, false, cancellationToken).ConfigureAwait(false);
        var idTile = IdTileStack.FirstLayer.GetTile(idRange.End - 1);
        var tile = await GetTile(chatId, ChatEntryKind.Text, idTile.Range, false, cancellationToken).ConfigureAwait(false);
        var lastEntry = tile.Entries.Length != 0 ? tile.Entries[^1] : null;
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
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbChatEntries = dbContext.ChatEntries
            .Where(e => e.ChatId == chatId.Value && e.Kind == entryKind);
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

        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var dbChatEntries = dbContext.ChatEntries
            .Where(e => e.ChatId == chatId.Value && e.Kind == entryKind)
            .Where(e => !e.IsThreadEntry);
        if (!includeRemoved)
            dbChatEntries = dbChatEntries.Where(e => !e.IsRemoved);
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
                .Collect(cancellationToken)
                .ConfigureAwait(false);
            return new ChatTile(smallerChatTiles, includeRemoved);
        }
        if (!includeRemoved) {
            var fullTile = await GetTile(chatId, entryKind, idTileRange, true, cancellationToken).ConfigureAwait(false);
            return new ChatTile(idTileRange, false, fullTile.Entries.Where(e => !e.IsRemoved).ToArray());
        }

        // If we're here, it's the smallest tile & includeRemoved = true
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var idRange = idTile.Range;
        var dbEntries = await dbContext.ChatEntries
            .Where(e => e.ChatId == chatId.Value
                && e.Kind == entryKind
                && e.LocalId >= idRange.Start
                && e.LocalId < idRange.End
                && !e.IsThreadEntry)
            .OrderBy(e => e.LocalId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Audio or video entries can't have any attachments
        if (entryKind != ChatEntryKind.Text)
            return new ChatTile(
                idTileRange,
                true,
                dbEntries
                    .Select(dbe => dbe.ToModel())
                    .ToArray());

        var allAttachmentsTask = GetAttachments();
        var allLinkPreviewsTask = GetLinkPreviews();

        await Task.WhenAll(allAttachmentsTask, allLinkPreviewsTask).ConfigureAwait(false);

        var allAttachments = await allAttachmentsTask.ConfigureAwait(false);
        var allLinkPreviews = await allLinkPreviewsTask.ConfigureAwait(false);
        var entries = dbEntries.Select(e => {
            var entryId = TextEntryId.Parse(e.Id);
            var entryAttachments = allAttachments[entryId];
            var linkPreviews = e.GetLinkPreviewIds()
                .Select(previewId => allLinkPreviews.GetValueOrDefault(previewId))
                .SkipNullItems()
                .ToArray();
            return e.ToModel(entryAttachments, linkPreviews);
        });
        return new ChatTile(idTileRange, true, entries.ToArray());

        Task<IReadOnlyDictionary<Symbol, LinkPreview>> GetLinkPreviews()
        {
            var linkPreviewIds  = dbEntries.Where(x => !x.LinkPreviewIds.IsNullOrEmpty())
                .SelectMany(x => x.GetLinkPreviewIds())
                .Distinct()
                .ToList();
            return linkPreviewIds.Count > 0
                ? GetLinkPreviewsBulk()
                : EmptyLinkPreviewsTask;

            async Task<IReadOnlyDictionary<Symbol, LinkPreview>> GetLinkPreviewsBulk()
            {
                var linkPreviews = await linkPreviewIds
                    .Select(id => LinkPreviewsBackend.Get(id, true, cancellationToken))
                    .Collect(cancellationToken)
                    .ConfigureAwait(false);
                return linkPreviews.SkipNullItems().ToDictionary(lp => lp.Id);
            }
        }

        Task<ILookup<TextEntryId, TextEntryAttachment>> GetAttachments()
        {
            var entryIdsWithAttachments = dbEntries.Where(x => x.HasAttachments)
                .Select(x => TextEntryId.Parse(x.Id))
                .ToList();

            return entryIdsWithAttachments.Count > 0
                ? GetAttachmentsBulk()
                : EmptyAttachmentsTask;

            async Task<ILookup<TextEntryId,TextEntryAttachment>> GetAttachmentsBulk() {
                var attachments = await entryIdsWithAttachments
                    .Select(x => GetEntryAttachments(x, cancellationToken))
                    .Collect(cancellationToken)
                    .ConfigureAwait(false);
                return attachments.SelectMany(x => x).ToLookup(x => x.EntryId);
            }
        }
    }

    // [ComputeMethod]
    public virtual async Task<ChatRangeMeta> GetChatRangeMeta(ChatId chatId, long idTileStart, CancellationToken cancellationToken)
    {
        var tile = IdTileStack.LastLayer.AssertIsTileStart(idTileStart);

        Range<long> chatIdRange;
        using (Computed.BeginIsolation())
            chatIdRange = await GetIdRange(chatId, ChatEntryKind.Text, false, cancellationToken).ConfigureAwait(false);
        var start = tile.Start;
        var end = tile.End;
        var entryIdRanges = new List<Range<long>>();
        var conversationIdRanges = new List<Range<long>>();
        var minCount = 0;
        var entryRangeMetaTask = GetEntryRangeMeta(chatId, idTileStart, cancellationToken);
        var conversationRangeMetaTask = ConversationsBackend.GetRangeMeta(chatId, idTileStart, cancellationToken);
        await Task.WhenAll(entryRangeMetaTask, conversationRangeMetaTask).ConfigureAwait(false);

        var entryRangeMeta = await entryRangeMetaTask.ConfigureAwait(false);
        var conversationRangeMeta = await conversationRangeMetaTask.ConfigureAwait(false);
        entryIdRanges.AddRange(entryRangeMeta.EntryRanges);
        conversationIdRanges.AddRange(conversationRangeMeta.ConversationRanges);
        minCount += EstimateMinimumCount(entryRangeMeta, conversationRangeMeta);
        var hasFulfilled = minCount >= Constants.Chat.MinChatPageMapSize || new Range<long>(start, end).Contains(chatIdRange);

        var previousEntryRangeMeta = entryRangeMeta;
        var previousConversationRangeMeta = conversationRangeMeta;
        var nextEntryRangeMeta = entryRangeMeta;
        var nextConversationRangeMeta = conversationRangeMeta;
        long previousId;
        long nextId;
        while (!hasFulfilled) {
            previousId = Math.Max(previousEntryRangeMeta?.PreviousEntryId ?? 0, (previousConversationRangeMeta?.PreviousConversationRange?.End ?? 1) - 1);
            nextId = Math.Min(nextEntryRangeMeta?.NextEntryId ?? long.MaxValue, nextConversationRangeMeta?.NextConversationRange?.Start ?? long.MaxValue);
            if (previousId == 0 && nextId == long.MaxValue)
                break;

            var previousTile = IdTileStack.LastLayer.GetTile(previousId);
            var nextTile = IdTileStack.LastLayer.GetTile(nextId);

            // Starting tasks
            var prevEntryRangeMetaTask = previousId != 0
                ? GetEntryRangeMeta(chatId, previousTile.Start, cancellationToken)
                : null;
            var prevConversationRangeMetaTask = previousId != 0
                ? ConversationsBackend.GetRangeMeta(chatId, previousTile.Start, cancellationToken)
                : null;
            var nextEntryRangeMetaTask = nextId != long.MaxValue
                ? GetEntryRangeMeta(chatId, nextTile.Start, cancellationToken)
                : null;
            var nextConversationRangeMetaTask = nextId != long.MaxValue
                ? ConversationsBackend.GetRangeMeta(chatId, nextTile.Start, cancellationToken)
                : null;

            previousEntryRangeMeta = prevEntryRangeMetaTask != null
                ? await prevEntryRangeMetaTask.ConfigureAwait(false)
                : null;
            previousConversationRangeMeta = prevConversationRangeMetaTask is not null
                ? await prevConversationRangeMetaTask.ConfigureAwait(false)
                : null;

            if (previousEntryRangeMeta is not null && previousConversationRangeMeta is not null) {
                start = previousTile.Start;
                entryIdRanges.AddRange(previousEntryRangeMeta.EntryRanges);
                conversationIdRanges.AddRange(previousConversationRangeMeta.ConversationRanges);
                minCount += EstimateMinimumCount(previousEntryRangeMeta, previousConversationRangeMeta);
                hasFulfilled = minCount >= Constants.Chat.MinChatPageMapSize || new Range<long>(start, end).Contains(chatIdRange);
                if (hasFulfilled)
                    break;
            }
            else
                start = chatIdRange.Start;

            nextEntryRangeMeta = nextEntryRangeMetaTask is not null
                ? await nextEntryRangeMetaTask.ConfigureAwait(false)
                : null;
            nextConversationRangeMeta = nextConversationRangeMetaTask is not null
                ? await nextConversationRangeMetaTask.ConfigureAwait(false)
                : null;
            if (nextEntryRangeMeta is null || nextConversationRangeMeta is null) {
                end = chatIdRange.End;
                continue;
            }

            end = nextTile.End;
            entryIdRanges.AddRange(nextEntryRangeMeta.EntryRanges);
            conversationIdRanges.AddRange(nextConversationRangeMeta.ConversationRanges);
            minCount += EstimateMinimumCount(nextEntryRangeMeta, nextConversationRangeMeta);
            hasFulfilled = minCount >= Constants.Chat.MinChatPageMapSize || new Range<long>(start, end).Contains(chatIdRange);
        }

        previousId = Math.Max(previousEntryRangeMeta?.PreviousEntryId ?? 0, (previousConversationRangeMeta?.PreviousConversationRange?.End ?? 1) - 1);
        nextId = Math.Min(nextEntryRangeMeta?.NextEntryId ?? long.MaxValue, nextConversationRangeMeta?.NextConversationRange?.Start ?? long.MaxValue);
        entryIdRanges.Sort((a, b) => a.Start.CompareTo(b.Start));
        conversationIdRanges.Sort((a, b) => a.Start.CompareTo(b.Start));

        // Merge adjacent entryIdRanges into a new collection
        // to avoid duplicates and reduce the number of ranges
        var mergedEntryIdRanges = entryIdRanges
            .MergeAdjacentRanges()
            .ToList();

        // Deduplicate conversationIdRanges by Start into a new collection
        var mergedConversationIdRanges = conversationIdRanges
            .EnsureMonotonic(RangeExt.LongRangeComparer)
            .ToList();

        return new ChatRangeMeta(
            new Range<long>(start, end),
            mergedEntryIdRanges.EnsureMonotonic(RangeExt.LongRangeComparer).ToArray(),
            mergedConversationIdRanges.EnsureMonotonic(RangeExt.LongRangeComparer).ToArray(),
            minCount,
            previousId == 0 ? null : IdTileStack.LastLayer.GetTile(previousId).Start,
            nextId == long.MaxValue ? null : IdTileStack.LastLayer.GetTile(nextId).Start);

        int EstimateMinimumCount(ChatEntryRangeMeta entryRangeMeta1, ConversationRangeMeta conversationRangeMeta1)
        {
            var count = 0;
            var lastRange = new Range<long>(0, 0);
            var merged = entryRangeMeta1.EntryRanges
                .Merge(conversationRangeMeta1.ConversationRanges, (ce, co) => ce.IntersectWith(co).IsEmpty ? (int)(ce.Start - co.Start) : 0)
                .ToList();

            Range<long>? pendingRight = null; // right part of the current entryRange
            Range<long> currentEntryRange = default;

            foreach (var (entryRange, conversationRange) in merged) {
                var hasEntryRange = !entryRange.IsEmpty;
                var hasConversationRange = !conversationRange.IsEmpty;

                // If we start processing a NEW entryRange, flush the pending right-hand side
                var conversationStartRange = new Range<long>(conversationRange.Start, conversationRange.Start + 1);
                if (hasEntryRange) {
                    if (entryRange == currentEntryRange && hasConversationRange) {
                        var (l, r) = (pendingRight ?? default).Subtract(conversationRange);
                        AddRangeCount(l);
                        AddRangeCount(conversationStartRange);
                        pendingRight = r;
                    }
                    else {
                        AddRangeCount(pendingRight ?? default);
                        pendingRight = null;
                        currentEntryRange = entryRange;
                    }
                }

                if (hasEntryRange && hasConversationRange) {
                    if (entryRange.Contains(conversationRange))
                        AddRangeCount(conversationStartRange);
                    else {
                        var (l, r) = entryRange.Subtract(conversationRange);
                        AddRangeCount(l);
                        AddRangeCount(conversationStartRange);
                        pendingRight = r;
                    }
                }
                else if (hasEntryRange)
                    AddRangeCount(entryRange);
                else if (hasConversationRange)
                    AddRangeCount(conversationStartRange);
            }
            AddRangeCount(pendingRight ?? default);
            return count;

            void AddRangeCount(Range<long> range)
            {
                if (lastRange.End > range.Start)
                    return;

                count += (int)range.Size();
                lastRange = range;
            }
        }
    }

    // [ComputeMethod]
    public virtual async Task<ChatEntryRangeMeta> GetEntryRangeMeta(
        ChatId chatId,
        long idTileStart,
        CancellationToken cancellationToken)
    {
        var idTile = IdTileStack.LastLayer.AssertIsTileStart(idTileStart);
        var idTileRange = idTile.Range;

        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var entryIds = await dbContext.ChatEntries
            .Where(e => e.ChatId == chatId.Value
                && e.Kind == ChatEntryKind.Text
                && e.LocalId >= idTileRange.Start
                && e.LocalId < idTileRange.End
                && !e.IsRemoved)
            .OrderBy(e => e.LocalId)
            .Select(e => e.LocalId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var previousEntryId = await dbContext.ChatEntries
            .Where(e => e.ChatId == chatId.Value
                && e.Kind == ChatEntryKind.Text
                && e.LocalId < idTileRange.Start
                && !e.IsRemoved)
            .MaxAsync(e => (long?)e.LocalId, cancellationToken)
            .ConfigureAwait(false);

        var nextEntryId = await dbContext.ChatEntries
            .Where(e => e.ChatId == chatId.Value
                && e.Kind == ChatEntryKind.Text
                && e.LocalId >= idTileRange.End
                && !e.IsRemoved)
            .MinAsync(e => (long?)e.LocalId, cancellationToken)
            .ConfigureAwait(false);

        var entryRanges = new List<Range<long>>();
        long? startId = null, endId = null;
        foreach (var entryId in entryIds) {
            if (startId == null)
                startId = entryId;
            else if (entryId != endId + 1) {
                entryRanges.Add(new Range<long>(startId.Value, endId!.Value + 1));
                startId = entryId;
            }
            endId = entryId;
        }
        if (startId != null && endId != null)
            entryRanges.Add(new Range<long>(startId.Value, endId.Value + 1));

        return new ChatEntryRangeMeta(chatId, entryRanges.ToArray(), previousEntryId, nextEntryId);
    }

    // [ComputeMethod]
    public virtual async Task<ChatCopyState?> GetChatCopyState(ChatId chatId, CancellationToken cancellationToken)
    {
        var dbCopiedChat = await DbChatCopyStateResolver.Get(chatId.Value, cancellationToken).ConfigureAwait(false);
        return dbCopiedChat?.ToModel();
    }

    // [ComputeMethod]
    public virtual async Task<ChatId?> GetForwardChatReplacement(
        ChatId sourceChatId,
        CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var chatCopyStates = await dbContext.ChatCopyStates
            .Where(c => c.SourceChatId == sourceChatId.Value && c.IsPublished)
            .OrderByDescending(c => c.PublishedAt)
            .ThenByDescending(c => c.LastEntryId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var chatCopyState in chatCopyStates) {
            var chat = await Get(ChatId.Parse(chatCopyState.Id), cancellationToken).ConfigureAwait(false);
            if (chat != null)
                return chat.Id;
        }
        return null;
    }

    // [ComputeMethod]
    public virtual async Task<ReadPositionsStatBackend?> GetReadPositionsStat(ChatId chatId, CancellationToken cancellationToken)
    {
        var dbReadPositionsStat = await DbReadPositionsStatResolver.Get(chatId.Value, cancellationToken)
            .ConfigureAwait(false);
        if (dbReadPositionsStat == null)
            return null;

        return new ReadPositionsStatBackend(chatId, dbReadPositionsStat.StartTrackingEntryLid, dbReadPositionsStat.GetTopReadPositions());
    }

    // [ComputeMethod]
    public virtual async Task<PlaceChatId?> GetPlaceChatIdByAlias(PlaceId placeId, AliasId aliasId, CancellationToken cancellationToken)
    {
        var chatIds = await ListPlaceChatIds(placeId, cancellationToken).ConfigureAwait(false);
        foreach (var chatId in chatIds) {
            var chat = await Get(chatId, cancellationToken).ConfigureAwait(false);
            if (chat is not null && chat.AliasId == aliasId)
                return chatId;
        }
        return null;
    }

    [ComputeMethod]
    protected virtual async Task<TextEntryAttachment[]> GetEntryAttachments(TextEntryId entryId, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var idPrefix = DbTextEntryAttachment.IdPrefix(entryId);
        var dbAttachments = await dbContext.TextEntryAttachments
            .Where(x => x.Id.StartsWith(idPrefix))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var mediaIds = dbAttachments.Select(x => x.MediaId)
            .Concat(dbAttachments.Select(x => x.ThumbnailMediaId))
            .Select(MediaId.ParseNullable)
            .SkipNullItems()
            .ToList();
        var mediaMap = EmptyMediaMap;
        if (mediaIds.Count > 0) {
            var mediaList = await mediaIds
                .Select(mid => MediaBackend.Get(mid, cancellationToken))
                .Collect(cancellationToken)
                .ConfigureAwait(false);
            mediaMap = mediaList
                .SkipNullItems()
                .DistinctBy(m => m.Id)
                .ToDictionary(m => m.Id);
        }
        return dbAttachments.Select(x => WithMedia(x.ToModel())).SkipNullItems().ToArray();

        TextEntryAttachment? WithMedia(TextEntryAttachment attachment)
        {
            var media = mediaMap.GetValueOrDefault(attachment.MediaId);
            if (media is null)
                return null;

            Media.Media? thumbnailMedia = null;
            if (attachment.ThumbnailMediaId != null)
                thumbnailMedia = mediaMap.GetValueOrDefault(attachment.ThumbnailMediaId);
            return attachment with {
                Media = media,
                ThumbnailMedia = thumbnailMedia,
            };
        }
    }

    // Non-compute methods

    // TODO: Chat and ChatFull. This method must return Chat
    // Not a [ComputeMethod]!
    public async Task<Chat[]> List(
        Moment minCreatedAt,
        ChatId? lastChatId,
        int limit,
        CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);
        var dMinCreatedAt = minCreatedAt.ToDateTime(DateTime.MinValue, DateTime.MaxValue);
        var dbChats = await dbContext.Chats
            .Where(x => x.CreatedAt >= dMinCreatedAt)
            .OrderBy(x => x.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (dbChats.Count == 0)
            return [];

        if (lastChatId is null || dbChats[0].CreatedAt > dMinCreatedAt)
            // no chats created at minCreatedAt that we need to skip
            return dbChats.Select(x => x.ToModel()).ToArray();

        var lastChatIdx = dbChats.FindIndex(x => ChatId.Parse(x.Id) == lastChatId);
        if (lastChatIdx < 0)
            return dbChats.Select(x => x.ToModel()).ToArray();

        return dbChats.Skip(lastChatIdx + 1).Select(x => x.ToModel()).ToArray();
    }

    // Not a [ComputeMethod]!
    public async Task<Chat[]> ListChanged(ChangedChatsQuery query, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var chatsQuery = query.LastId is null
            ? dbContext.Chats.Where(x => x.Version >= query.MinVersion && x.Version <= query.MaxVersion)
            : dbContext.Chats.Where(x => (x.Version > query.MinVersion && x.Version <= query.MaxVersion)
                || (x.Version == query.MinVersion && string.Compare(x.Id, query.LastId.Value) > 0));

        var dbChats = await chatsQuery
            .WhereIf(x => !x.Id.StartsWith(PeerChatId.IdPrefix), query.ExcludePeerChats)
            .WhereIf(x => !x.IsPlaceRootChat, query.ExcludePlaceRootChats)
            .OrderBy(x => x.Version)
            .ThenBy(x => x.Id)
            .Take(query.Limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return dbChats.Select(x => x.ToModel()).ToArray();
    }

    // Not a [ComputeMethod]!
    public async Task<ChatEntry[]> ListChangedEntries(ChangedEntriesQuery query, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var entriesQuery = query.LastLocalId <= 0
            ? dbContext.ChatEntries.Where(x => x.Version >= query.MinVersion && x.Version <= query.MaxVersion)
            : dbContext.ChatEntries.Where(x
                => (x.Version > query.MinVersion && x.Version <= query.MaxVersion)
                || (x.Version == query.MinVersion && x.LocalId > query.LastLocalId));

        return await entriesQuery
            .Where(x => x.ChatId == query.ChatId.Value && x.Kind == ChatEntryKind.Text)
            .OrderBy(x => x.Version)
            .ThenBy(x => x.LocalId)
            .Take(query.Limit)
            .AsAsyncEnumerable()
            .Select(x => x.ToModel())
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    // Not a [ComputeMethod]!
    public async Task<ChatEntry[]> ListNewEntries(
        ChatId chatId,
        long minLocalIdExclusive,
        int limit,
        CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbEntries = await dbContext.ChatEntries.Where(x
                => x.ChatId == chatId.Value
                && x.Kind == ChatEntryKind.Text
                && x.LocalId > minLocalIdExclusive)
            .OrderBy(x => x.LocalId)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return dbEntries
            .Select(x => x.ToModel())
            .ToArray();
    }

    // Not a [ComputeMethod]!
    public async Task<ChatEntry[]> ListEntries(
        ChatId chatId,
        Moment from,
        CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbEntries = await dbContext.ChatEntries
            .Where(x => x.ChatId == chatId.Value
                && x.Kind == ChatEntryKind.Text
                && x.BeginsAt >= from.ToDateTime())
            .OrderBy(x => x.LocalId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return dbEntries
            .Select(x => x.ToModel())
            .ToArray();
    }

    // [CommandHandler]
    public virtual async Task<Chat> OnChange(
        ChatsBackend_Change command,
        CancellationToken cancellationToken)
    {
        var (chatId, expectedVersion, change, ownerId) = command;
        var context = CommandContext.GetCurrent();

        if (Invalidation.IsActive) {
            var invChat = context.Operation.Items.KeylessGet<Chat>();
            if (invChat != null) {
                _ = Get(invChat.Id, default);
                if (invChat is { TemplateId: not null, TemplatedForUserId: not null })
                    _ = GetTemplatedChatFor(invChat.TemplateId, invChat.TemplatedForUserId, default);
                if (invChat.Id is PlaceChatId invPlaceChatId) {
                    _ = GetPublicChatIdsFor(invPlaceChatId.PlaceId, default);
                    if (!invPlaceChatId.IsRoot && change.Kind != ChangeKind.Update)
                        _ = ListPlaceChatIds(invPlaceChatId.PlaceId, default);
                }
            }
            return null!;
        }

        change.RequireValid();
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbChat = chatId is null ? null :
            await dbContext.Chats.ForUpdate()
                // ReSharper disable once AccessToModifiedClosure
                .FirstOrDefaultAsync(c => c.Id == chatId.Value, cancellationToken)
                .ConfigureAwait(false);
        var oldChat = dbChat?.ToModel();
        Chat chat;
        if (change.IsCreate(out var update)) {
            oldChat.RequireNull();
            var placeId = update.PlaceId;
            var chatKind = update.Kind ?? (chatId is null && placeId is not null ? ChatKind.Place : chatId?.Kind ?? ChatKind.Group);

            if (chatKind == ChatKind.Thread) {
                /* Accept provided chat id. */
            }
            else if (chatKind == ChatKind.Group) {
                if (chatId is null)
                    chatId = GroupChatId.New();
                else if (!chatId.IsSystem)
                    throw new ArgumentOutOfRangeException(nameof(command), "Invalid ChatId.");
            }
            else if (chatKind == ChatKind.Place) {
                if (chatId is null) {
                    if (placeId is null) // No place is created yet, so we're creating its root chat
                        chatId = PlaceId.New().RootChatId;
                    else if (OrdinalEquals(Constants.Chat.SystemTags.Welcome, update.SystemTag))
                        chatId = PlaceChatId.Parse(PlaceChatId.Format(placeId, Constants.Chat.SystemTags.Welcome));
                    else
                        chatId = PlaceChatId.New(placeId);
                }
                else if (!(chatId is PlaceChatId placeChatId && placeChatId.IsRoot))
                    throw new ArgumentOutOfRangeException(nameof(command),
                        "Change.ChatId must be null for new place chats.");
                update.ValidateForPlaceChat();
            }
            else if (chatKind != ChatKind.Peer)
                throw new ArgumentOutOfRangeException(nameof(command), "Invalid Change.Kind.");

            if (update.IsArchived.HasValue)
                throw new ArgumentOutOfRangeException(nameof(command), "Invalid Change.IsArchived.");

            chat = new Chat(chatId.Require()) {
                CreatedAt = Clocks.SystemClock.Now,
            };
            chat = ApplyDiff(chat, update);
            dbChat = new DbChat(chat);
            if (!dbChat.SystemTag.IsNullOrEmpty()
                && Constants.Chat.SystemTags.Rules.MustBeUniquePerUser(dbChat.SystemTag)) {
                // Only group chats can have system tags
                ownerId.Require("Command.OwnerId");
                // Chats with system tags should be unique per user except Welcome chat.
                var existingDbChat = await dbContext.Chats
                    .Join(dbContext.Authors, c => c.Id, a => a.ChatId, (c, a) => new { c, a })
                    .Where(x => x.a.UserId == ownerId.Value && x.c.SystemTag == dbChat.SystemTag)
                    .Select(x => x.c)
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (existingDbChat != null)
                    return existingDbChat.ToModel();
            }

            dbContext.Add(dbChat);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await UpdateAlias(oldChat, chat).ConfigureAwait(false);

            if (chatId is PeerChatId peerChatId) {
                // Peer chat
                ownerId.RequireNull();

                // Creating authors
                await peerChatId.UserIds
                    .ToArray()
                    .Select(userId => AuthorsBackend.EnsureJoined(chatId, userId, cancellationToken))
                    .Collect(cancellationToken)
                    .ConfigureAwait(false);
            }
            else if (chatId.Kind == ChatKind.Group || chatId.Kind == ChatKind.Place) {
                // Group chat
                ownerId.Require("Command.OwnerId");
                // If the chat is created with the option to join anonymously, we join its owner as anonymous author
                var upsertCommand = new AuthorsBackend_Upsert(
                    chatId, default, ownerId, null,
                    new AuthorDiff {
                        IsAnonymous = chat.AllowAnonymousAuthors
                    });
                var author = await Commander.Call(upsertCommand, cancellationToken).ConfigureAwait(false);

                if (chat.HasSingleAuthor)
                    await AddSingleAuthorRole(chatId, author).ConfigureAwait(false);
                else {
                    await CreateOwnerRole(chatId, author).ConfigureAwait(false);
                    await CreateAnyoneRole(chatId).ConfigureAwait(false);
                }

                if (chat.IsAiSearchChat()) {
                    var upsertMlBotAuthorCommand = new AuthorsBackend_Upsert(
                        chat.Id, default, Constants.User.Sherlock.UserId, null,
                        new AuthorDiff()
                    );
                    _ = await Commander.Call(upsertMlBotAuthorCommand, cancellationToken).ConfigureAwait(false);
                }
            }
            else if (chatId.Kind == ChatKind.Thread) {
                ownerId.Require("Command.OwnerId");
                var threadChatId = (ThreadChatId)chatId;
                var author = await AuthorsBackend
                    .GetByUserId(threadChatId.GetOutermostParent(), ownerId, RequestedAuthorKind.Full, cancellationToken)
                    .Require()
                    .ConfigureAwait(false);

                await CreateOwnerRole(chatId, author).ConfigureAwait(false);
            }
            else
                throw new ArgumentOutOfRangeException(nameof(command), "Invalid ChatId.");
        }
        else if (change.IsUpdate(out update)) {
            chatId.Require();
            ownerId.RequireNull();
            update.PlaceId.RequireNull();

            dbChat.RequireVersion(expectedVersion);
            if (chatId.Kind is ChatKind.Place) {
                update.ValidateForPlaceChat();
                if (OrdinalEquals(Constants.Chat.SystemTags.Welcome, dbChat.SystemTag)
                    && update.IsPublic == false)
                    throw StandardError.Constraint("Can't change chat type to private for 'Welcome' chat.");
            }

            chat = ApplyDiff(dbChat.ToModel(), update);
            dbChat.UpdateFrom(chat);

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await UpdateAlias(oldChat, chat).ConfigureAwait(false);
        }
        else if (change.IsRemove()) {
            chatId.Require();
            dbChat.Require();

            if (OrdinalEquals(Constants.Chat.SystemTags.Welcome, dbChat.SystemTag))
                throw StandardError.Constraint("It's prohibited to remove 'Welcome' chat.");

            if (!dbChat.MediaId.IsNullOrEmpty()) {
                var removeMediaCommand = new MediaBackend_Change(
                    MediaId.Parse(dbChat.MediaId),
                    new Change<Media.Media> { Remove = true });
                await Commander.Call(removeMediaCommand, true, cancellationToken).ConfigureAwait(false);
            }
            var attachmentMediaIds = await dbContext.ChatEntries
                .Where(ce => ce.ChatId == chatId.Value && ce.HasAttachments)
                .Join(dbContext.TextEntryAttachments, ce => ce.Id, ea => ea.EntryId, (_, ea) => ea.MediaId)
                .Distinct()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (var mediaSid in attachmentMediaIds) {
                var mediaId = MediaId.Parse(mediaSid);
                if (!OrdinalEquals(mediaId.Scope, chatId.Value))
                    continue; // NOTE(DF): Do not remove media from current chat scope. Forwarded messages can contain media from another chat.

                var removeMediaCommand = new MediaBackend_Change(
                    mediaId,
                    new Change<Media.Media> { Remove = true });
                await Commander.Call(removeMediaCommand, true, cancellationToken).ConfigureAwait(false);
            }
            // Remove attachments
            await dbContext.ChatEntries
                .Where(ce => ce.ChatId == chatId.Value && ce.HasAttachments)
                .Join(dbContext.TextEntryAttachments, ce => ce.Id, ea => ea.EntryId, (_, ea) => ea)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
            // Remove reaction summaries
            await dbContext.ChatEntries
                .Where(ce => ce.ChatId == chatId.Value)
                .Join(dbContext.ReactionSummaries, ce => ce.Id, rs => rs.EntryId, (_, rs) => rs)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
            // Remove reactions
            await dbContext.ChatEntries
                .Where(ce => ce.ChatId == chatId.Value)
                .Join(dbContext.Reactions, ce => ce.Id, rs => rs.EntryId, (_, rs) => rs)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
            // Remove mentions
            await dbContext.ChatEntries
                .Where(ce => ce.ChatId == chatId.Value)
                .Join(dbContext.Mentions.Where(m => m.ChatId == chatId.Value), ce => ce.LocalId, rs => rs.EntryLocalId, (_, rs) => rs)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
            // Remove entries
            await dbContext.ChatEntries
                .Where(ce => ce.ChatId == chatId.Value)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
            // Remove roles
            await dbContext.Roles
                .Where(r => r.ChatId == chatId.Value)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
            // Remove authors
            if (!chatId.IsThread()) {
                // Remove authors
                var removeAuthorsCommand = new AuthorsBackend_Remove(chatId, null, null);
                await Commander.Call(removeAuthorsCommand, false, cancellationToken).ConfigureAwait(false);
            }
            else {
                // Thread chat does not own authors. It uses authors from the parent chat.
            }
            dbContext.Remove(dbChat);

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await RemoveAlias(dbChat.ToModel()).ConfigureAwait(false);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        chat = dbChat.Require().ToModel();
        context.Operation.Items.KeylessSet(chat);

        // Raise events
        context.Operation.AddEvent(new ChatChangedEvent(chat, oldChat, change.Kind));
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
            case ChatKind.Thread:
                if (newChat.Title.IsNullOrEmpty())
                    throw StandardError.Constraint("Thread chat title cannot be empty.");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(command), "Invalid chat kind.");
            }
            return newChat;
        }

        async Task UpdateAlias(Chat? oldChat1, Chat chat1)
        {
            if (chat1.Id.Kind == ChatKind.Peer) {
                if (chat1.AliasId != null)
                    throw StandardError.NotSupported("Custom links are not allowed for place chats.");
                return;
            }

            var oldAliasId = oldChat1?.AliasId;
            var aliasId = chat1.IsPublic ? chat1.AliasId : null;

            if (chat1.Id.Kind == ChatKind.Group)
                await Commander
                    .UpdateAlias(oldAliasId, aliasId, AliasKind.Chat, chat1.Id.Value, cancellationToken)
                    .ConfigureAwait(false);
            else if (chat1.Id is PlaceChatId placeChatId1) {
                // Validate that alias isn't used by some other place chat
                if (aliasId is not null && aliasId != oldAliasId) {
                    var placeChatId = await GetPlaceChatIdByAlias(placeChatId1.PlaceId, aliasId, cancellationToken)
                        .ConfigureAwait(false);
                    if (placeChatId is not null && placeChatId != chat1.Id)
                        throw StandardError.Constraint($"Custom link '{aliasId.Value}' is already used for another chat in the same Place.");
                }
            }
        }

        async Task RemoveAlias(Chat oldChat1)
        {
            var oldAliasId = oldChat1.AliasId;
            if (oldAliasId is null)
                return;

            if (oldChat1.Id.Kind == ChatKind.Group)
                await Commander
                    .UpdateAlias(oldAliasId, null, AliasKind.Chat, "", cancellationToken)
                    .ConfigureAwait(false);
        }

        async Task AddSingleAuthorRole(ChatId chatId1, AuthorFull author)
        {
            var createCustomRoleCmd = new RolesBackend_Change(chatId1, default, null, new() {
                Create = new RoleDiff {
                    Name = "SingleAuthor",
                    SystemRole = SystemRole.None,
                    Permissions = ChatPermissions.Write,
                    AuthorIds = new SetDiff<AuthorId[], AuthorId>() {
                        AddedItems = [author.Id],
                    },
                },
            });
            await Commander.Call(createCustomRoleCmd, cancellationToken).ConfigureAwait(false);
        }

        async Task CreateOwnerRole(ChatId chatId2, AuthorFull author)
        {
            var createOwnerRoleCmd = new RolesBackend_Change(chatId2, default, null, new() {
                Create = new RoleDiff {
                    SystemRole = SystemRole.Owner,
                    Permissions = ChatPermissions.Owner,
                    AuthorIds = new SetDiff<AuthorId[], AuthorId>() {
                        AddedItems = [author.Id],
                    },
                },
            });
            await Commander.Call(createOwnerRoleCmd, cancellationToken).ConfigureAwait(false);
        }

        async Task CreateAnyoneRole(ChatId chatId3)
        {
            var createAnyoneRoleCmd = new RolesBackend_Change(chatId3, default, null, new () {
                Create = new RoleDiff() {
                    SystemRole = SystemRole.Anyone,
                    Permissions =
                        ChatPermissions.Write
                        | ChatPermissions.Invite
                        | ChatPermissions.SeeMembers
                        | ChatPermissions.Leave,
                },
            });
            await Commander.Call(createAnyoneRoleCmd, cancellationToken).ConfigureAwait(false);
        }
    }

    // [CommandHandler]
    public virtual async Task<ChatEntry> OnChangeEntry(ChatsBackend_ChangeEntry command, CancellationToken cancellationToken)
    {
        var change = command.Change;
        var chatEntryId = command.ChatEntryId;
        var chatId = chatEntryId.ChatId.Require();
        var changeKind = change.Kind;
        var entryKind = chatEntryId.Kind;
        var expectedVersion = command.ExpectedVersion;
        var context = CommandContext.GetCurrent();

        if (Invalidation.IsActive) {
            var invChatEntry = context.Operation.Items.KeylessGet<ChatEntry>();
            if (invChatEntry != null) {
                InvalidateTiles(chatId, entryKind, invChatEntry.LocalId, changeKind);

                var entryTile = IdTileStack.LastLayer.GetTile(invChatEntry.LocalId);
                _ = GetEntryRangeMeta(chatId, entryTile.Range.Start, default);

                var previousEntryId = context.Operation.Items.Get<long>(nameof(ChatEntryRangeMeta.PreviousEntryId));
                var nextEntryId = context.Operation.Items.Get<long>(nameof(ChatEntryRangeMeta.NextEntryId));
                if (previousEntryId != 0 && !entryTile.Range.Contains(previousEntryId)) {
                    var previousEntryIdTile = IdTileStack.LastLayer.GetTile(previousEntryId);
                    _ = GetEntryRangeMeta(chatId, previousEntryIdTile.Range.Start, default);
                }
                if (nextEntryId != 0 && !entryTile.Range.Contains(nextEntryId)) {
                    var nextIdTile = IdTileStack.LastLayer.GetTile(nextEntryId);
                    _ = GetEntryRangeMeta(chatId, nextIdTile.Range.Start, default);
                }
            }

            // Invalidate min-max Id range at last
            switch (changeKind) {
            case ChangeKind.Create:
                var createdChatEntry = context.Operation.Items.Get<ChatEntryId>(CreatedChatEntryId);
                if (createdChatEntry is { LocalId: <= 1 })
                    _ = GetMinId(createdChatEntry.ChatId, entryKind, default);
                _ = GetIdRange(chatId, entryKind, true, default);
                _ = GetIdRange(chatId, entryKind, false, default);
                break;
            case ChangeKind.Remove:
                _ = GetIdRange(chatId, entryKind, false, default);
                break;
            }
            return null!;
        }

        change.RequireValid();
        ChatEntry entry;
        ChatEntry? oldEntry;
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using (var __ = dbContext.ConfigureAwait(false)) {
            var dbEntry = changeKind == ChangeKind.Create
                ? null
                : await dbContext.ChatEntries.ForUpdate()
                    // ReSharper disable once AccessToModifiedClosure
                    .FirstOrDefaultAsync(c => c.Id == chatEntryId.Value, cancellationToken)
                    .ConfigureAwait(false);
            oldEntry = dbEntry?.ToModel();

            if (chatId is PeerChatId peerChatId)
                _ = await EnsureExists(peerChatId, cancellationToken).ConfigureAwait(false);

            if (change.IsCreate(out var update)) {
                chatId.Require();
                var localId = await DbNextLocalId(dbContext, chatId, entryKind, cancellationToken)
                    .ConfigureAwait(false);
                chatEntryId = ChatEntryId.New(chatId, entryKind, localId);
                entry = new ChatEntry(chatEntryId) {
                    Version = VersionGenerator.NextVersion(),
                    BeginsAt = Clocks.SystemClock.Now,
                };
                entry = ApplyDiff(entry, update, false);
                entry = await PrepareTextEntryForSave(entry, oldEntry, cancellationToken).ConfigureAwait(false);
                dbEntry = new DbChatEntry(entry) {
                    HasAttachments = entry.Attachments.Length > 0,
                };
                dbContext.Add(dbEntry);
                context.Operation.Items.Set(CreatedChatEntryId, chatEntryId);

                if (entryKind == ChatEntryKind.Text)
                    await StorePreviousAndNextEntryIds(localId).ConfigureAwait(false);
            }
            else if (change.IsUpdate(out update)) {
                dbEntry.RequireVersion(expectedVersion);
                if (dbEntry.IsRemoved && update.IsRemoved == true)
                    throw StandardError.Constraint("Removed chat entries cannot be modified.");

                entry = ApplyDiff(dbEntry.ToModel(), update, true) with {
                    Version = VersionGenerator.NextVersion(dbEntry.Version),
                };
                entry = await PrepareTextEntryForSave(entry, oldEntry, cancellationToken).ConfigureAwait(false);
                var hasAttachments = update.Attachments is { Length: > 0 } || dbEntry.HasAttachments;
                dbEntry.UpdateFrom(entry);
                dbEntry.HasAttachments = hasAttachments;
            }
            else if (change.IsRemove()) {
                dbEntry.Require();
                entry = oldEntry.Require();
                if (!entry.IsRemoved) {
                    entry = entry with {
                        IsRemoved = true,
                        Version = VersionGenerator.NextVersion(dbEntry.Version),
                    };
                    dbEntry.UpdateFrom(entry);

                    var localId = entry.LocalId;
                    if (entryKind == ChatEntryKind.Text)
                        await StorePreviousAndNextEntryIds(localId).ConfigureAwait(false);
                }
            }
            else
                throw StandardError.Internal("Invalid ChatEntryDiff state.");

            context.Operation.Items.KeylessSet(entry);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            entry = dbEntry.ToModel().WithPopulatedValues(entry);
        }

        if (entryKind != ChatEntryKind.Text)
            return entry;

        if (chatId is PlaceChatId { IsRoot: false })
            await EnsurePlaceChatAuthorExists(entry.AuthorId).ConfigureAwait(false);
        if (changeKind == ChangeKind.Remove) {
            await EnqueueChangedEvent().ConfigureAwait(false);
            return entry;
        }

        if (entry.IsStreaming)
            return entry;

        if (changeKind == ChangeKind.Create)
            AppMeters.MessageCount.Add(1);

        if (change.IsCreate(out var create) && create.Attachments is { Length: > 0 } attachments) {
            var textEntryAttachments = attachments
                .Select((x, i) => new TextEntryAttachment {
                    EntryId = chatEntryId.ToTextEntryId(),
                    Index = i,
                    MediaId = x.MediaId,
                    ThumbnailMediaId = x.ThumbnailMediaId,
                })
                .ToArray();
            var createAttachmentsCmd = new ChatsBackend_CreateAttachments(textEntryAttachments);
            var createdAttachments = await Commander.Call(createAttachmentsCmd, cancellationToken).ConfigureAwait(false);
            entry = entry with { Attachments = createdAttachments };
        }

        // Let's enqueue the TextEntryChangedEvent
        await EnqueueChangedEvent().ConfigureAwait(false);
        return entry;

        ChatEntry ApplyDiff(ChatEntry originalEntry, ChatEntryDiff? diff, bool isUpdate)
        {
            var oldAuthorId = originalEntry.AuthorId;
            var newEntry = DiffEngine.Patch(originalEntry, diff) with {
                Version = VersionGenerator.NextVersion(originalEntry.Version),
            };
            if (newEntry.Kind != originalEntry.Kind)
                throw StandardError.Constraint("Chat Entry kind cannot be changed.");

            // Validation
            switch (newEntry.Kind) {
            case ChatEntryKind.Audio:
                if (newEntry.AudioEntryLid.HasValue)
                    throw StandardError.Constraint("Audio entry should not have AudioEntryLid.");
                if (newEntry.VideoEntryLid.HasValue)
                    throw StandardError.Constraint("Audio entry should not have VideoEntryLid.");
                if (newEntry.RepliedEntryLid.HasValue)
                    throw StandardError.Constraint("Audio entry should not have RepliedEntryLocalId.");
                if (newEntry.ForwardedChatEntryId is not null)
                    throw StandardError.Constraint("Audio entry should not have ForwardedChatEntryId.");
                if (newEntry.ForwardedAuthorId is not null)
                    throw StandardError.Constraint("Audio entry should not have ForwardedAuthorId.");
                if (newEntry.Attachments.Length != 0)
                    throw StandardError.Constraint("Audio entry should not have Attachments.");
                if (newEntry.LinkPreviewIds.Length != 0)
                    throw StandardError.Constraint("Audio entry should not have LinkPreviewId.");
                break;
            case ChatEntryKind.Text:
                if (!isUpdate)
                    break;

                if (newEntry.AuthorId != oldAuthorId)
                    throw StandardError.Unauthorized("You can edit only your own messages.");
                if (diff?.Content != null && newEntry.IsStreaming)
                    throw StandardError.Constraint("Only text messages can be edited.");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(command), "Invalid chat entry kind.");
            }
            return newEntry;
        }

        async Task EnsurePlaceChatAuthorExists(AuthorId authorId1) {
            var author1 = await AuthorsBackend
                .Get(authorId1.ChatId, authorId1, RequestedAuthorKind.Default, cancellationToken)
                .ConfigureAwait(false);
            if (author1 is { HasLeft: false })
                return;

            var author2 = await AuthorsBackend
                .Get(authorId1.ChatId, authorId1, RequestedAuthorKind.Full, cancellationToken)
                .Require()
                .ConfigureAwait(false);
            var accountId = author2.UserId.Require();

            var upsertCommand = new AuthorsBackend_Upsert(
                authorId1.ChatId, authorId1, accountId, null,
                new AuthorDiff() {
                    IsAnonymous = false,
                    HasLeft = false,
                });
            await Commander.Call(upsertCommand, true, cancellationToken).ConfigureAwait(false);
        }

        async Task EnqueueChangedEvent() {
            var authorId = entry.AuthorId;
            var author = await AuthorsBackend.Get(authorId.ChatId, authorId, RequestedAuthorKind.Full, cancellationToken).ConfigureAwait(false);
            context.Operation.AddEvent(new TextEntryChangedEvent(entry, author!, changeKind, oldEntry));
        }

        async Task StorePreviousAndNextEntryIds(long localEntryLid)
        {
            var previousEntryId = await dbContext.ChatEntries
                .Where(c => c.ChatId == chatId.Value && c.Kind == ChatEntryKind.Text && !c.IsRemoved && c.LocalId < localEntryLid)
                .OrderByDescending(c => c.LocalId)
                .Select(c => c.LocalId)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            var nextEntryId = await dbContext.ChatEntries
                .Where(c => c.ChatId == chatId.Value && c.Kind == ChatEntryKind.Text && !c.IsRemoved && c.LocalId > localEntryLid)
                .OrderBy(c => c.LocalId)
                .Select(c => c.LocalId)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (previousEntryId != 0)
                context.Operation.Items.Set(nameof(ChatEntryRangeMeta.PreviousEntryId), previousEntryId);
            if (nextEntryId != 0)
                context.Operation.Items.Set(nameof(ChatEntryRangeMeta.NextEntryId), nextEntryId);
        }
    }

    // [CommandHandler]
    public virtual async Task<TextEntryAttachment[]> OnCreateAttachments(
        ChatsBackend_CreateAttachments command,
        CancellationToken cancellationToken)
    {
        var attachments = command.Attachments;
        if (attachments.Length > Constants.Attachments.FileCountLimit)
            throw StandardError.Constraint("Too many attachments in bulk.");

        var entryIds = attachments.Select(x => x.EntryId).Distinct().ToList();
        if (entryIds.Count > 1)
            throw StandardError.Constraint("Attachments cannot belong to different messages.");

        var entryId = entryIds[0];

        if (Invalidation.IsActive) {
            _ = GetEntryAttachments(entryId, default);
            InvalidateTiles(entryId.ChatId, ChatEntryKind.Text, entryId.LocalId, ChangeKind.Update);
            return default!;
        }

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbAttachments = new List<DbTextEntryAttachment>();
        foreach (var attachment in attachments) {
            var dbChatEntry = await dbContext.ChatEntries.Get(entryId.Value, cancellationToken)
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
        return dbAttachments.Select(x => x.ToModel()).ToArray();
    }

    // [CommandHandler]
    public virtual async Task OnRemoveOwnChats(
        ChatsBackend_RemoveOwnChats command,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var userId = command.UserId;
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var chatSidsToDelete = new List<string>();
        var ownChatSids = await dbContext.Chats
            .Join(dbContext.Roles, c => c.Id, r => r.ChatId, (c, r) => new { c, r })
            .Join(dbContext.AuthorRoles, x => x.r.Id, r => r.DbRoleId, (x, r) => new { x.c, x.r, ar = r })
            .Join(dbContext.Authors, x => x.ar.DbAuthorId, a => a.Id, (x, a) => new { x.c, x.r, x.ar, a })
            .Where(x => x.a.UserId == userId.Value && x.r.SystemRole == SystemRole.Owner)
            .Select(x => x.c.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var chatSid in ownChatSids) {
            var hasOtherOwners = await dbContext.Chats
                .Join(dbContext.Roles, c => c.Id, r => r.ChatId, (c, r) => new { c, r })
                .Join(dbContext.AuthorRoles, x => x.r.Id, r => r.DbRoleId, (x, r) => new { x.c, x.r, ar = r })
                .Join(dbContext.Authors, x => x.ar.DbAuthorId, a => a.Id, (x, a) => new { x.c, x.r, x.ar, a })
                .Where(x => x.c.Id == chatSid && x.a.UserId != userId.Value && x.r.SystemRole == SystemRole.Owner)
                .AnyAsync(cancellationToken)
                .ConfigureAwait(false);

            if (!hasOtherOwners)
                chatSidsToDelete.Add(chatSid);
        }
        foreach (var chatSid in chatSidsToDelete) {
            var deleteChatCommand = new ChatsBackend_Change(
                ChatId.Parse(chatSid),
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

        if (Invalidation.IsActive) {
            var invChats = context.Operation.Items.KeylessGet<Dictionary<string,long>>();
            if (invChats == null)
                return;

            var tileSize = Constants.Chat.ServerIdTileStack.MinTileSize;
            foreach (var chatEntryPair in invChats) {
                var chatId = ChatId.Parse(chatEntryPair.Key);
                var entryId = chatEntryPair.Value;
                InvalidateTiles(chatId, ChatEntryKind.Text, entryId, ChangeKind.Remove);
                InvalidateTiles(chatId, ChatEntryKind.Text, entryId - tileSize, ChangeKind.Remove);
                InvalidateTiles(chatId, ChatEntryKind.Text, entryId - tileSize*2, ChangeKind.Remove);
                InvalidateTiles(chatId, ChatEntryKind.Text, entryId - tileSize*3, ChangeKind.Remove);
                InvalidateTiles(chatId, ChatEntryKind.Text, entryId - tileSize*4, ChangeKind.Remove);
                _ = GetEntryAttachments(TextEntryId.New(chatId, entryId), default);
            }
            return;
        }

        var chatEntriesToInvalidate = new Dictionary<string, long>(StringComparer.Ordinal);
        var userId = command.UserId;
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var chatAuthors = await dbContext.Authors
            .Where(a => a.UserId == userId.Value)
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
                    MediaId.Parse(mediaId),
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
                .Join(dbContext.Mentions.Where(m => m.ChatId == chatId), ce => ce.LocalId, rs => rs.EntryLocalId, (_, rs) => rs)
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

        context.Operation.Items.KeylessSet(chatEntriesToInvalidate);
    }

    public virtual async Task OnCreateNotesChat(ChatsBackend_CreateNotesChat command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var userId = command.UserId;
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        await dbContext.Chats
            .LockShared(userId, Constants.Chat.SystemTags.Notes, cancellationToken)
            .ConfigureAwait(false);

        var hasNotesChat = await dbContext.Chats
            .Join(dbContext.Authors, c => c.Id, a => a.ChatId, (c, a) => new { c, a })
            .AnyAsync(x => x.a.UserId == userId.Value && x.c.SystemTag == Constants.Chat.SystemTags.Notes.Value, cancellationToken)
            .ConfigureAwait(false);

        if (hasNotesChat)
            return;

        await dbContext.Chats
            .Lock(userId, Constants.Chat.SystemTags.Notes, cancellationToken)
            .ConfigureAwait(false);

        var createNotesCommand = new ChatsBackend_Change(
            null,
            null,
            new Change<ChatDiff> {
                Create = new ChatDiff {
                    Title = "Notes",
                    Kind = ChatKind.Group,
                    IsPublic = false,
                    MediaId = MediaId.Parse("system-icons:notes"),
                    IsTemplate = false,
                    AllowGuestAuthors = false,
                    AllowAnonymousAuthors = false,
                    SystemTag = Constants.Chat.SystemTags.Notes,
                },
            },
            userId);
        await Commander.Call(createNotesCommand, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<ChatCopyState> OnChangeChatCopyState(ChatsBackend_ChangeChatCopyState command, CancellationToken cancellationToken)
    {
        var change = command.Change;
        var chatId = command.ChatId;
        var expectedVersion = command.ExpectedVersion;

        if (Invalidation.IsActive) {
            _ = GetChatCopyState(chatId, default);
            return null!;
        }

        change.RequireValid();
        ChatCopyState chatCopyState;
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);
        var dbChatCopyState = await dbContext.ChatCopyStates.ForUpdate()
            // ReSharper disable once AccessToModifiedClosure
            .FirstOrDefaultAsync(c => c.Id == chatId.Value, cancellationToken)
            .ConfigureAwait(false);
        var oldChatCopyState = dbChatCopyState?.ToModel();

        if (change.IsCreate(out var update)) {
            oldChatCopyState.RequireNull();

            chatCopyState = new ChatCopyState(chatId) {
                CreatedAt = Clocks.SystemClock.Now,
            };
            chatCopyState = DiffEngine.Patch(chatCopyState, update) with {
                Version = VersionGenerator.NextVersion(),
            };
            if (chatCopyState.SourceChatId is null)
                throw StandardError.Constraint("SourceChatId should be specified to create ChatCopyState.");

            dbChatCopyState = new DbChatCopyState(chatCopyState);
            dbContext.Add(dbChatCopyState);
        }
        else if (change.IsUpdate(out update)) {
            dbChatCopyState.RequireVersion(expectedVersion);
            if (update.SourceChatId.HasValue)
                throw StandardError.Constraint("SourceChatId can't be edited.");
            var originalChatCopyState = dbChatCopyState.ToModel();
            if (originalChatCopyState.IsPublished)
                throw StandardError.Constraint("ChatCopyState can't be edited after it has been marked as published.");

            chatCopyState = DiffEngine.Patch(originalChatCopyState, update) with {
                Version = VersionGenerator.NextVersion(),
            };

            if (update.IsPublished == true)
                chatCopyState = chatCopyState with {
                    PublishedAt = Clocks.SystemClock.Now,
                };

            if (update.IsCopiedSuccessfully.HasValue || update.LastProcessedEntryId.HasValue)
                chatCopyState = chatCopyState with {
                    LastCopyingAt = Clocks.SystemClock.Now,
                };

            dbChatCopyState.UpdateFrom(chatCopyState);
        }
        else if (change.IsRemove()) {
            dbChatCopyState.Require();
            throw StandardError.Constraint("Removing ChatCopyState is not allowed.");
        }
        else
            throw StandardError.Internal("Invalid ChatCopyStateDiff state.");

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return chatCopyState;
    }

    public virtual async Task OnUpdateReadPositionsStat(
        ChatsBackend_UpdateReadPositionsStat command,
        CancellationToken cancellationToken)
    {
        var chatId = command.ChatId;
        var context = CommandContext.GetCurrent();

        if (Invalidation.IsActive) {
            if (context.Operation.Items.KeylessGet<bool>())
                _ = GetReadPositionsStat(chatId, default);
            return;
        }

        var userId = command.UserId;
        var positionId = command.PositionId;

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);
        await dbContext.ReadPositionsStats.Lock(chatId, cancellationToken).ConfigureAwait(false);
        var dbReadPositionsStat = await dbContext.ReadPositionsStats
            .FirstOrDefaultAsync(c => c.ChatId == chatId.Value, cancellationToken)
            .ConfigureAwait(false);

        var hasChanges = false;
        if (dbReadPositionsStat != null) {
            if (dbReadPositionsStat.StartTrackingEntryLid <= positionId) {
                var items = dbReadPositionsStat.GetTopReadPositions().ToList();
                if (items.Count == 0) {
                    items.Add(new UserReadPosition(userId, positionId));
                    hasChanges = true;
                }
                else {
                    if (items.Count == 1 || items[^1].EntryLid < positionId) {
                        var index = items.FindIndex(c => c.UserId == userId);
                        if (index >= 0) {
                            if (items[index].EntryLid < positionId) {
                                items[index] = new UserReadPosition(userId, positionId);
                                hasChanges = true;
                            }
                        }
                        else {
                            items.Add(new UserReadPosition(userId, positionId));
                            hasChanges = true;
                        }
                    }
                }
                if (hasChanges) {
                    items = items
                        .OrderByDescending(c => c.EntryLid)
                        .ThenBy(c => c.UserId)
                        .Take(2)
                        .ToList();
                    var top1 = items[0];
                    var top2 = items.Count > 1 ? items[1] : null;
                    dbReadPositionsStat.Version = VersionGenerator.NextVersion(dbReadPositionsStat.Version);
                    dbReadPositionsStat.Top1UserId = top1.UserId.Value;
                    dbReadPositionsStat.Top1EntryLid = top1.EntryLid;
                    dbReadPositionsStat.Top2UserId = top2?.UserId.Value ?? "";
                    dbReadPositionsStat.Top2EntryLid = top2?.EntryLid ?? 0;
                }
            }
        }
        else {
            var idRange = await GetIdRange(chatId, ChatEntryKind.Text, false, cancellationToken).ConfigureAwait(false);
            var lastEntryId = idRange.End - 1; // Start tracking positions stat since this entry
            var shouldTrackPosition = positionId >= lastEntryId;
            dbContext.Add(new DbReadPositionsStat() {
                ChatId = chatId.Value,
                Version = VersionGenerator.NextVersion(),
                StartTrackingEntryLid = lastEntryId,
                Top1UserId = shouldTrackPosition ? userId.Value : "",
                Top1EntryLid = shouldTrackPosition ? positionId : 0,
            });
            hasChanges = true;
        }

        if (hasChanges) {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            context.Operation.Items.KeylessSet(true);
        }
    }

    // Event handlers

    // [EventHandler]
    public virtual async Task OnNewUserEvent(NewUserEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var isDevelopmentInstance = HostInfo.IsDevelopmentInstance;
        var isTested = HostInfo.IsTested;

        // If we aren't running tests, we always join Announcements chat,
        // + try joining other dev-only chats if they exist.
        var joinAnnouncementsChat = true;
        var joinNotesChat = true;
        var joinDefaultChat = isDevelopmentInstance;
        var joinFeedbackTemplateChat = isDevelopmentInstance;
        if (isTested) {
            // If we're running tests, these options are matching to ChatDbInitializer.Options.AddXxx
            var options = Services.GetService<ChatDbInitializer.Options>() ?? ChatDbInitializer.Options.Default;
            joinAnnouncementsChat = options.AddAnnouncementsChat;
            joinNotesChat = options.AddNotesChat;
            joinDefaultChat = options.AddDefaultChat;
            joinFeedbackTemplateChat = options.AddFeedbackTemplateChat;
        }

        var userId = eventCommand.UserId;
        if (joinAnnouncementsChat)
            await JoinAnnouncementsChat(userId, cancellationToken).ConfigureAwait(false);
        if (joinDefaultChat)
            await JoinDefaultChatIfAdmin(userId, cancellationToken).ConfigureAwait(false);
        if (joinNotesChat)
            await CreateNotesChat(userId, cancellationToken).ConfigureAwait(false);
        if (joinFeedbackTemplateChat)
            await JoinFeedbackTemplateChatIfAdmin(userId, cancellationToken).ConfigureAwait(false);
    }

    // [EventHandler]
    public virtual async Task OnAuthorChangedEvent(AuthorUpsertedEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var (author, oldAuthor) = eventCommand;
        if (author.ChatId == Constants.Chat.AnnouncementsChatId || author.ChatId.Kind == ChatKind.Peer)
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

        // and public place chats
        if (chat is { IsPublic: true, Id: PlaceChatId { IsRoot: false } })
            return;

        // Reading the current author; we may need to wait for its creation here, so...
        AuthorFull? readAuthor = null;
        var retrier = new Retrier(5, RetryDelaySeq.Exp(0.25, 1));
        while (retrier.NextOrThrow()) {
            await Clocks.CoarseCpuClock.Delay(retrier.Delay, cancellationToken).ConfigureAwait(false);
            readAuthor = await AuthorsBackend.Get(author.ChatId, author.Id, RequestedAuthorKind.Full, cancellationToken).ConfigureAwait(false);
            if (readAuthor?.Avatar != null)
                break;
        }
        var isAnonymous = readAuthor?.IsAnonymous ?? author.IsAnonymous;
        var authorId = isAnonymous ? null : author.Id;
        var authorName = isAnonymous ? "Someone" : readAuthor?.Avatar.Name;
        if (authorName.IsNullOrEmpty())
            authorName = MentionMarkup.NotAvailableName;

        var entryId = TextEntryId.New(author.ChatId, 0);
        var command = new ChatsBackend_ChangeEntry(
            entryId,
            null,
            Change.Create(new ChatEntryDiff {
                AuthorId = Bots.GetWalleId(author.ChatId),
                SystemEntry = (SystemEntry)new MembersChangedOption(authorId, authorName, author.HasLeft),
            }));

        await Commander.Call(command, true, cancellationToken).ConfigureAwait(false);
    }

    // [EventHandler]
    public virtual async Task OnPlaceRemoved(PlaceChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var (_, oldPlace, kind) = eventCommand;
        if (kind != ChangeKind.Remove)
            return;

        var placeId = oldPlace.Require().Id;
        var chatIds = await ListPlaceChatIds(placeId, cancellationToken).ConfigureAwait(false);
        foreach (var chatId in chatIds) {
            var chat = await Get(chatId, cancellationToken).ConfigureAwait(false);
            if (chat != null && OrdinalEquals(Constants.Chat.SystemTags.Welcome, chat.SystemTag)) {
                var resetChatTagCommand = new ChatsBackend_Change(chatId, null, new Change<ChatDiff> {
                    Update = new ChatDiff { SystemTag = Symbol.Empty }
                });
                await Commander.Call(resetChatTagCommand, false, cancellationToken).ConfigureAwait(false);
            }
            var deleteChatCommand = new ChatsBackend_Change(chatId, null, new Change<ChatDiff> { Remove = true });
            await Commander.Call(deleteChatCommand, false, cancellationToken).ConfigureAwait(false);
        }
    }

    // [EventHandler]
    public virtual async Task OnChatChangedEvent(ChatChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var (chat, oldChat, kind) = eventCommand;
        if (chat.Id.IsThread(out var threadChatId) && kind == ChangeKind.Remove) {
            var startThreadEntryId = TextEntryId.New(threadChatId, threadChatId.ThreadId);
            var chatEntry = await this.GetEntry(startThreadEntryId, cancellationToken).ConfigureAwait(false);
            if (chatEntry is not null && chatEntry.IsThreadStartEntry) {
                var markChatEntryAsRemoved = new ChatsBackend_ChangeEntry(startThreadEntryId,
                    null,
                    Change.Update(new ChatEntryDiff { IsRemoved = true }));
                await Commander.Call(markChatEntryAsRemoved, true, cancellationToken).ConfigureAwait(false);
            }
        }
        if (kind == ChangeKind.Remove || chat.IsSummarized == false)
            // TODO(AK): Check if we need any events to stop flow
            return;

        if (NeedsSummarization())
            await Flows.GetOrStart<ConversationSplitFlow>(chat.Id.Value, cancellationToken).ConfigureAwait(false);
        return;

        bool NeedsSummarization()
            => chat.IsSummarized == true && oldChat?.IsSummarized != true;
    }

    // [EventHandler]
    public virtual async Task OnTextEntryChangedEvent(TextEntryChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var (entry, _, kind, _) = eventCommand;

        await Summarize().ConfigureAwait(false);
        return;

        async Task Summarize()
        {
            if (!Settings.IsSummarizationEnabled)
                return;

            var chat = await Get(entry.ChatId, cancellationToken).ConfigureAwait(false);
            if (chat == null)
                return;

            if (chat.IsSummarized == false || kind == ChangeKind.Remove)
                return;

            var endsAt = entry.GetEndsAt();
            var timeSinceEnded = Clocks.SystemClock.Now - endsAt;
            var splitFlow = await Flows.GetAndResume<ConversationSplitFlow>(chat.Id.Value,
                    timeSinceEnded + Settings.ChatEntrySummarizationDelay,
                    $"{nameof(OnTextEntryChangedEvent)} #{entry.Id}",
                    timeSinceEnded + Settings.ChatEntrySummarizationDelay,
                    cancellationToken)
                .ConfigureAwait(false);
            if (splitFlow == null) // Recreate flow if it was removed
                await Flows.GetOrStart<ConversationSplitFlow>(chat.Id.Value, cancellationToken).ConfigureAwait(false);
        }
    }

    // Protected methods

    [ComputeMethod]
    protected virtual async Task<long> GetMinId(
        ChatId chatId,
        ChatEntryKind entryKind,
        CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        return await dbContext.ChatEntries
            .Where(e => e.ChatId == chatId.Value && e.Kind == entryKind)
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

        if (entryKind == ChatEntryKind.Text && changeKind is ChangeKind.Create or ChangeKind.Remove) {
            // Invalidate GetEntryRangeMeta
            var tile = IdTileStack.LastLayer.GetTile(entryId);
            _ = GetEntryRangeMeta(chatId, tile.Start, default);
        }
    }

    protected virtual async Task<AuthorRules> GetRulesRaw(
        ChatId chatId,
        PrincipalId principalId,
        CancellationToken cancellationToken)
    {
        var chat = await Get(chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return AuthorRules.None(chatId);

        UserId? userId;
        AccountFull? account;
        AuthorFull? author;
        if (principalId is UserId principalUserId) {
            userId = principalUserId;
            account = await AccountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);
            if (account == null)
                return AuthorRules.None(chatId);

            author = await AuthorsBackend.GetByUserId(chatId, account.Id, RequestedAuthorKind.Default, cancellationToken).ConfigureAwait(false);
        }
        else if (principalId is AuthorId authorId) {
            userId = null;
            author = await AuthorsBackend.Get(chatId, authorId, RequestedAuthorKind.Default, cancellationToken).ConfigureAwait(false);
            if (author != null)
                userId = author.UserId;
            else {
                if (chatId is PlaceChatId { IsRoot: false } placeChatId) {
                    var rootChatId = placeChatId.PlaceId.RootChatId;
                    var rootChatAuthorId = AuthorId.New(rootChatId, authorId.LocalId);
                    var rootAuthor = await AuthorsBackend
                        .Get(rootChatId, rootChatAuthorId, RequestedAuthorKind.Default, cancellationToken)
                        .ConfigureAwait(false);
                    if (rootAuthor != null)
                        userId = rootAuthor.UserId;
                }
            }

            if (userId is not null)
                account = await AccountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);
            else
                account = null;
            if (account == null)
                return AuthorRules.None(chatId);
        }
        else
            return AuthorRules.None(chatId);

        var roles = Array.Empty<Role>();
        var isJoined = author is { HasLeft: false };
        if (isJoined) {
            var isGuest = account.IsGuest;
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
        if (chatId.Kind != ChatKind.Peer && !rules.CanRead()) {
            // Has invite = same as having read permission
            var hasActivated = await HasActivatedInvite(account.Id, chatId, cancellationToken).ConfigureAwait(false);
            if (hasActivated)
                rules = rules with {
                    Permissions = (rules.Permissions | ChatPermissions.Join).AddImplied(),
                };
        }

        if (chat.IsChatRoulette()) {
            const ChatPermissions mask =
                ChatPermissions.Read |
                ChatPermissions.Write |
                ChatPermissions.SeeMembers |
                ChatPermissions.Leave;
            rules = rules with { Permissions = rules.Permissions & mask };
            var hasCompleted = false;
            var chatRouletteId = await RouletteExt.GetChatRouletteId(chatId, AuthorsBackend, cancellationToken)
                .ConfigureAwait(false);
            if (chatRouletteId is null)
                hasCompleted = true;
            else {
                var chatRoulette = await RouletteBackend.GetChatRoulette(chatRouletteId, cancellationToken).ConfigureAwait(false);
                if (chatRoulette is null || chatRoulette.CompletedBy is not null)
                    hasCompleted = true;
            }
            if (hasCompleted)
                rules = rules with { Permissions = rules.Permissions & ~ChatPermissions.Write }; // Disable write when roulette marked as completed.
        }
        return rules;
    }

    [ComputeMethod]
    protected virtual async Task<AuthorRules> GetPlaceChatRules(
        PlaceChatId placeChatId,
        PrincipalId principalId,
        CancellationToken cancellationToken)
    {
        var chatId = (ChatId)placeChatId;
        var chat = await Get(chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return AuthorRules.None(chatId);

        var place = await PlacesBackend.Get(placeChatId.PlaceId, cancellationToken).ConfigureAwait(false);
        if (place == null)
            return AuthorRules.None(chatId);

        var rootChatId = placeChatId.PlaceId.RootChatId;
        var rootChatPrincipalId = ActualChat.Chat.AuthorsBackend.Remap(principalId, rootChatId);
        var rootChatRules = await GetRules(rootChatId, rootChatPrincipalId, cancellationToken).ConfigureAwait(false);
        if (rootChatRules.Account is not { } account)
            return AuthorRules.None(chatId);
        if (!rootChatRules.CanRead())
            return AuthorRules.None(chatId);

        var isPlaceMember = rootChatRules.Author is { HasLeft: false };
        var directRules = await GetRulesRaw(chatId, principalId, cancellationToken).ConfigureAwait(false);
        if (!isPlaceMember) {
            if (chat.IsPublic && directRules.CanRead())
                return new AuthorRules(chat.Id, directRules.Author, account, ChatPermissions.Read);
            return AuthorRules.None(chatId);
        }

        var author = await AuthorsBackend
            .GetByUserId(chatId, account.Id, RequestedAuthorKind.Full, cancellationToken)
            .ConfigureAwait(false);
        if (chat.IsPublic) {
            var permissions = rootChatRules.Permissions & ~ChatPermissions.Leave; // Do not allow leaving public chat on a place
            return new AuthorRules(chat.Id, author, account, permissions);
        }

        return new AuthorRules(chat.Id, author, account, directRules.Permissions);
    }

    // Private / internal methods

    private async Task<ChatEntry> PrepareTextEntryForSave(ChatEntry entry, ChatEntry? existing, CancellationToken cancellationToken)
    {
        if (entry.IsSystemEntry || entry.IsStreaming || entry.Kind is not ChatEntryKind.Text)
            return entry;

        var wasContentChanged = !OrdinalEquals(entry.Content, existing?.Content ?? "");
        if (!wasContentChanged)
            return entry with {
                LinkPreviewIds = existing?.LinkPreviewIds ?? [],
                Content = existing?.Content ?? "",
            };

        // Inject mention names into the markup
        var chatMarkupHub = ChatMarkupHubFactory[entry.ChatId];
        return entry with {
            Content = await chatMarkupHub.PrepareForSave(entry, cancellationToken).ConfigureAwait(false),
            LinkPreviewIds = MarkupParser.ExtractLinkPreviewIds(entry),
        };
    }

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
        var chatId = Constants.Chat.FeedbackTemplateChatId;
        var chat = await Get(chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null) {
            Log.LogWarning("Feedback template chat is not found while trying to join {UserId}", userId);
            return;
        }

        var account = await AccountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);
        if (account is not { IsAdmin: true })
            return;

        var email = account.GetVerifiedEmail();
        if (email.IsNullOrEmpty())
            return;
        if (!email.OrdinalEndsWith(Constants.Team.EmailSuffix))
            return;

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
                    AuthorIds = new SetDiff<AuthorId[], AuthorId> {
                        AddedItems = [author.Id],
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
        if (principalId is UserId userId)
            account = await AccountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);
        else if (principalId is AuthorId authorId) {
            author = await AuthorsBackend.Get(chatId, authorId, RequestedAuthorKind.Default, cancellationToken).ConfigureAwait(false);
            if (author == null)
                return AuthorRules.None(chatId);

            account = await AccountsBackend.Get(author.UserId, cancellationToken).ConfigureAwait(false);
        }
        if (account is null)
            return AuthorRules.None(chatId);

        var otherUserId = chatId.AnotherUserIdOrNull(account.Id);
        if (otherUserId.IsGuestOrNull()) // No peer chats with guests
            return AuthorRules.None(chatId);

        if (account.IsGuestOrNull()) {
            // We grant guests permission to "read" the chat (which is going to be empty anyway)
            // solely to make sure ChatPage can display it like it already exists.
            // The footer there should contain "Sign in to chat" button in this case.
            // Once this guest signs in, he'll be redirected to the actual peer with otherUserId.
            return new(chatId, author, account, (ChatPermissions.SeeMembers | ChatPermissions.Join).AddImplied());
        }

        var permissions = (ChatPermissions.Write | ChatPermissions.SeeMembers | ChatPermissions.Join).AddImplied();
        return new(chatId, author, account, permissions);
    }

    private async Task<Chat> EnsureExists(PeerChatId peerChatId, CancellationToken cancellationToken)
    {
        var chat = await Get(peerChatId, cancellationToken).ConfigureAwait(false);
        if (chat.IsStored())
            return chat;

        var command = new ChatsBackend_Change(peerChatId, null, new() { Create = new ChatDiff() });
        chat = await Commander.Call(command, true, cancellationToken).ConfigureAwait(false);
        return chat;
    }

    private async Task<bool> HasActivatedInvite(UserId userId, ChatId chatId, CancellationToken cancellationToken)
    {
        var activationKey = await ServerKvasBackend
            .GetUserClient(userId)
            .Get<string>(ServerKvasInviteKey.ForChat(chatId), cancellationToken)
            .ConfigureAwait(false);
        if (activationKey is null)
            return false;

        return await InvitesBackend.IsValid(activationKey, cancellationToken).ConfigureAwait(false);
    }
}
