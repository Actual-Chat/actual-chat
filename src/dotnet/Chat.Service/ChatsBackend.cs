using ActualChat.Chat.Db;
using ActualChat.Chat.Flows;
using ActualChat.Chat.Module;
using ActualChat.Contacts;
using ActualChat.Db;
using ActualChat.Diagnostics;
using ActualChat.Flows;
using ActualChat.Hosting;
using ActualChat.Invite;
using ActualChat.Kvas;
using ActualChat.Users;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;
using ActualLab.Resilience;
using ActualLab.Rpc;

namespace ActualChat.Chat;

/// <summary>
/// Backend service implementation for chat operations including entries, tiles, and chat management.
/// </summary>
public partial class ChatsBackend(IServiceProvider services) : DbServiceBase<ChatDbContext>(services), IChatsBackend
{
    private const string CreatedChatEntryId = "CreatedChatEntryId";
    private static readonly TileStack<long> IdTileStack = Constants.Chat.ServerIdTileStack;
    private static readonly Dictionary<MediaId, Media.Media> EmptyMediaMap = new ();
    private static readonly ILookup<ChatEntryId, ChatEntryAttachment> EmptyAttachments
        = Array.Empty<ChatEntryAttachment>().ToLookup(ta => ta.EntryId);
    private static readonly Task<ILookup<ChatEntryId, ChatEntryAttachment>> EmptyAttachmentsTask
        = Task.FromResult(EmptyAttachments);
    private static readonly IReadOnlyDictionary<Symbol, LinkPreview> EmptyLinkPreviews
        = new Dictionary<Symbol, LinkPreview>().AsReadOnly();
    private static readonly Task<IReadOnlyDictionary<Symbol, LinkPreview>> EmptyLinkPreviewsTask
        = Task.FromResult(EmptyLinkPreviews);
    private static readonly IReadOnlyDictionary<string, ChatEntryAudio> EmptyAudioMap
        = new Dictionary<string, ChatEntryAudio>().AsReadOnly();
    private static readonly Task<IReadOnlyDictionary<string, ChatEntryAudio>> EmptyAudioMapTask
        = Task.FromResult(EmptyAudioMap);

    // all backend services should be requested lazily to avoid circular references!

    private IAccountsBackend AccountsBackend => field ??= Services.GetRequiredService<IAccountsBackend>();
    private IAuthorsBackend AuthorsBackend => field ??= Services.GetRequiredService<IAuthorsBackend>();
    private IRolesBackend RolesBackend => field ??= Services.GetRequiredService<IRolesBackend>();
    private IMediaBackend MediaBackend => field ??= Services.GetRequiredService<IMediaBackend>();
    private ILinkPreviewsBackend LinkPreviewsBackend => field ??= Services.GetRequiredService<ILinkPreviewsBackend>();
    private IInvitesBackend InvitesBackend => field ??= Services.GetRequiredService<IInvitesBackend>();
    private IPlacesBackend PlacesBackend => field ??= Services.GetRequiredService<IPlacesBackend>();
    private IConversationsBackend ConversationsBackend => field ??= Services.GetRequiredService<IConversationsBackend>();
    private IContactsBackend ContactsBackend => field ??= Services.GetRequiredService<IContactsBackend>();
    private IServerKvasBackend ServerKvasBackend => field ??= Services.GetRequiredService<IServerKvasBackend>();
    private HostInfo HostInfo => field ??= Services.HostInfo();
    private IMarkupParser MarkupParser => field ??= Services.GetRequiredService<IMarkupParser>();
    private KeyedFactory<IBackendChatMarkupHub, ChatId> ChatMarkupHubFactory => field ??= Services.KeyedFactory<IBackendChatMarkupHub, ChatId>();
    private IDbEntityResolver<string, DbChat> DbChatResolver => field ??= Services.GetRequiredService<IDbEntityResolver<string, DbChat>>();
    private IDbEntityResolver<string, DbChatCopyState> DbChatCopyStateResolver => field ??= Services.GetRequiredService<IDbEntityResolver<string, DbChatCopyState>>();
    private IDbEntityResolver<string, DbReadPositionsStat> DbReadPositionsStatResolver => field ??= Services.GetRequiredService<IDbEntityResolver<string, DbReadPositionsStat>>();
    private IDbShardLocalIdGenerator<DbChatEntry, string> DbChatEntryIdGenerator => field ??= Services.GetRequiredService<IDbShardLocalIdGenerator<DbChatEntry, string>>();
    private DiffEngine DiffEngine => field ??= Services.GetRequiredService<DiffEngine>();
    private FlowHub FlowHub => field ??= Services.FlowHub();
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
        return await dbContext.ChatEntries.Where(x => x.Id == sid && x.Kind == 0)
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
            return new AuthorRules(chatId, threadChatAuthor, account, threadPermissions.AddImplied());
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

        var idRange = await GetLidRange(chatId, false, cancellationToken).ConfigureAwait(false);
        var idTile = IdTileStack.FirstLayer.GetTile(idRange.End - 1);
        var tile = await GetTile(chatId, idTile.Range, false, cancellationToken).ConfigureAwait(false);
        var lastEntry = tile.Entries.Length != 0 ? tile.Entries[^1] : null;
        return new ChatNews(idRange, lastEntry);
    }

    // Note that it returns (firstId, lastId + 1) range!
    // [ComputeMethod]
    [LegacyName("GetIdRange", "2.7.9999")]
    public virtual async Task<Range<long>> GetLidRange(
        ChatId chatId,
        bool includeRemoved,
        CancellationToken cancellationToken)
    {
        var minLid = await GetMinLid(chatId, cancellationToken).ConfigureAwait(false);
        var maxLid = await GetMaxLid(chatId, includeRemoved, cancellationToken).ConfigureAwait(false);
        return (minLid, Math.Max(minLid, maxLid) + 1);
    }

    [ComputeMethod]
    public virtual async Task<long> GetMinLid(
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        return await dbContext.ChatEntries
            .Where(e => e.ChatId == chatId.Value && e.Kind == 0)
            .OrderBy(e => e.LocalId)
            .Select(e => e.LocalId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<long> GetMaxLid(
        ChatId chatId,
        bool includeRemoved,
        CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var dbChatEntries = dbContext.ChatEntries
            .Where(e => e.ChatId == chatId.Value && e.Kind == 0)
            .Where(e => !e.IsThreadEntry);
        if (!includeRemoved)
            dbChatEntries = dbChatEntries.Where(e => !e.IsRemoved);
        return await dbChatEntries
            .OrderByDescending(e => e.LocalId)
            .Select(e => e.LocalId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ApiSet<AuthorId>> GetFirstEntryAuthors(
        ChatId chatId,
        int entryCount,
        bool includeRemoved,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(entryCount, 64);
        if (entryCount <= 0)
            return ApiSet<AuthorId>.Empty;

        var minLid = await GetMinLid(chatId, cancellationToken).ConfigureAwait(false);
        var authors = new ApiSet<AuthorId>();
        var remainingCount = entryCount;
        for (var idTile = IdTileStack.FirstLayer.GetTile(minLid); remainingCount > 0; idTile = idTile.Next()) {
            var tile = await GetTile(chatId, idTile.Range, true, cancellationToken).ConfigureAwait(false);
            if (tile.Entries.Length == 0)
                break;

            foreach (var entry in tile.Entries) {
                if (!includeRemoved && entry.IsRemoved)
                    continue;

                authors.Add(entry.AuthorId);
                remainingCount--;
                if (remainingCount == 0)
                    break;
            }
        }
        return remainingCount == 0 ? authors : ApiSet<AuthorId>.Empty;
    }

    // [ComputeMethod]
    public virtual async Task<ChatTile> GetTile(
        ChatId chatId,
        Range<long> lidTileRange,
        bool includeRemoved,
        CancellationToken cancellationToken)
    {
        var idTile = IdTileStack.GetTile(lidTileRange);
        var smallerIdTiles = idTile.Smaller();
        if (smallerIdTiles.Length != 0) {
            var smallerChatTiles = await smallerIdTiles
                .Select(sidTile => GetTile(chatId,
                    sidTile.Range,
                    includeRemoved,
                    cancellationToken))
                .Collect(cancellationToken)
                .ConfigureAwait(false);
            return new ChatTile(smallerChatTiles, includeRemoved);
        }
        if (!includeRemoved) {
            var fullTile = await GetTile(chatId, lidTileRange, true, cancellationToken).ConfigureAwait(false);
            return new ChatTile(lidTileRange, false, fullTile.Entries.Where(e => !e.IsRemoved).ToArray());
        }

        // If we're here, it's the smallest tile & includeRemoved = true
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var idRange = idTile.Range;
        var dbEntries = await dbContext.ChatEntries
            .Where(e => e.ChatId == chatId.Value
                && e.Kind == 0
                && e.LocalId >= idRange.Start
                && e.LocalId < idRange.End
                && !e.IsThreadEntry)
            .OrderBy(e => e.LocalId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var allAttachmentsTask = GetAttachments(dbEntries, cancellationToken);
        var allLinkPreviewsTask = GetLinkPreviews();
        var allAudioTask = GetAudioMap(dbEntries, cancellationToken);

        await Task.WhenAll(allAttachmentsTask, allLinkPreviewsTask, allAudioTask).ConfigureAwait(false);

        var allAttachments = await allAttachmentsTask.ConfigureAwait(false);
        var allLinkPreviews = await allLinkPreviewsTask.ConfigureAwait(false);
        var allAudio = await allAudioTask.ConfigureAwait(false);
        var entries = dbEntries.Select(e => {
            var entryId = ChatEntryId.Parse(e.Id);
            var entryAttachments = allAttachments[entryId];
            var linkPreviews = e.DeserializeLinkPreviewIds()
                .Select(previewId => allLinkPreviews.GetValueOrDefault(previewId))
                .SkipNullItems()
                .ToArray();
            var entry = e.ToModel(entryAttachments, linkPreviews);
            // Enrich partial audio with MediaBackend-resolved data (BlobId, timing)
            if (entry.Audio?.MediaId is { } mid && !mid.Value.IsNullOrEmpty()
                && allAudio.TryGetValue(mid.Value, out var resolvedAudio))
                entry = entry with { Audio = resolvedAudio with { TimeMap = entry.Audio.TimeMap } };
            return entry;
        });
        return new ChatTile(lidTileRange, true, entries.ToArray());

        Task<IReadOnlyDictionary<Symbol, LinkPreview>> GetLinkPreviews()
        {
            var linkPreviewIds  = dbEntries.Where(x => !x.LinkPreviewIds.IsNullOrEmpty())
                .SelectMany(x => x.DeserializeLinkPreviewIds())
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
    }

    // [ComputeMethod]
    public virtual async Task<ChatRangeMeta> GetChatRangeMeta(ChatId chatId, long lidTileStart, CancellationToken cancellationToken)
    {
        var tile = IdTileStack.LastLayer.AssertIsTileStart(lidTileStart);

        Range<long> chatLidRange;
        using (Computed.BeginIsolation())
            chatLidRange = await GetLidRange(chatId, false, cancellationToken).ConfigureAwait(false);
        var startLid = tile.Start;
        var endLid = tile.End;
        var entryLidRanges = new List<Range<long>>();
        var conversationIdRanges = new List<Range<long>>();
        var minCount = 0;
        var entryRangeMetaTask = GetEntryRangeMeta(chatId, lidTileStart, cancellationToken);
        var conversationRangeMetaTask = ConversationsBackend.GetRangeMeta(chatId, lidTileStart, cancellationToken);
        await Task.WhenAll(entryRangeMetaTask, conversationRangeMetaTask).ConfigureAwait(false);

        var entryRangeMeta = await entryRangeMetaTask.ConfigureAwait(false);
        var conversationRangeMeta = await conversationRangeMetaTask.ConfigureAwait(false);
        entryLidRanges.AddRange(entryRangeMeta.EntryLidRange);
        conversationIdRanges.AddRange(conversationRangeMeta.ConversationLidRanges);
        minCount += EstimateMinimumCount(entryRangeMeta, conversationRangeMeta);
        var hasFulfilled = minCount >= Constants.Chat.MinChatPageMapSize || new Range<long>(startLid, endLid).Contains(chatLidRange);

        var previousEntryRangeMeta = entryRangeMeta;
        var previousConversationRangeMeta = conversationRangeMeta;
        var nextEntryRangeMeta = entryRangeMeta;
        var nextConversationRangeMeta = conversationRangeMeta;
        long previousId;
        long nextId;
        while (!hasFulfilled) {
            previousId = Math.Max(previousEntryRangeMeta?.PreviousEntryLid ?? 0, (previousConversationRangeMeta?.PreviousConversationLidRange?.End ?? 1) - 1);
            nextId = Math.Min(nextEntryRangeMeta?.NextEntryLid ?? long.MaxValue, nextConversationRangeMeta?.NextConversationLidRange?.Start ?? long.MaxValue);
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
                startLid = previousTile.Start;
                entryLidRanges = [..previousEntryRangeMeta.EntryLidRange, ..entryLidRanges];
                conversationIdRanges = [..previousConversationRangeMeta.ConversationLidRanges, ..conversationIdRanges];
                minCount += EstimateMinimumCount(previousEntryRangeMeta, previousConversationRangeMeta);
                hasFulfilled = minCount >= Constants.Chat.MinChatPageMapSize || new Range<long>(startLid, endLid).Contains(chatLidRange);
                if (hasFulfilled)
                    break;
            }
            else
                startLid = IdTileStack.LastLayer.GetTile(chatLidRange.Start).Start;

            nextEntryRangeMeta = nextEntryRangeMetaTask is not null
                ? await nextEntryRangeMetaTask.ConfigureAwait(false)
                : null;
            nextConversationRangeMeta = nextConversationRangeMetaTask is not null
                ? await nextConversationRangeMetaTask.ConfigureAwait(false)
                : null;
            if (nextEntryRangeMeta is null || nextConversationRangeMeta is null) {
                endLid = chatLidRange.End;
                continue;
            }

            endLid = nextTile.End;
            entryLidRanges.AddRange(nextEntryRangeMeta.EntryLidRange);
            conversationIdRanges.AddRange(nextConversationRangeMeta.ConversationLidRanges);
            minCount += EstimateMinimumCount(nextEntryRangeMeta, nextConversationRangeMeta);
            hasFulfilled = minCount >= Constants.Chat.MinChatPageMapSize || new Range<long>(startLid, endLid).Contains(chatLidRange);
        }

        previousId = Math.Max(previousEntryRangeMeta?.PreviousEntryLid ?? 0, (previousConversationRangeMeta?.PreviousConversationLidRange?.End ?? 1) - 1);
        nextId = Math.Min(nextEntryRangeMeta?.NextEntryLid ?? long.MaxValue, nextConversationRangeMeta?.NextConversationLidRange?.Start ?? long.MaxValue);
        entryLidRanges.Sort((a, b) => a.Start.CompareTo(b.Start));
        conversationIdRanges.Sort((a, b) => a.Start.CompareTo(b.Start));

        // Merge adjacent entryIdRanges into a new collection
        // to avoid duplicates and reduce the number of ranges
        var mergedEntryIdRanges = entryLidRanges
            .MergeAdjacentRanges()
            .ToList();

        // Deduplicate conversationIdRanges by Start into a new collection
        var mergedConversationIdRanges = conversationIdRanges
            .EnsureMonotonic()
            .ToList();

        return new ChatRangeMeta(
            new Range<long>(startLid, endLid),
            mergedEntryIdRanges.EnsureMonotonic().ToArray(),
            mergedConversationIdRanges.EnsureMonotonic().ToArray(),
            minCount,
            previousId == 0 ? null : IdTileStack.LastLayer.GetTile(previousId).Start,
            nextId == long.MaxValue ? null : IdTileStack.LastLayer.GetTile(nextId).Start);

        int EstimateMinimumCount(ChatEntryRangeMeta entryRangeMeta1, ConversationRangeMeta conversationRangeMeta1)
        {
            var count = 0;
            var lastRange = new Range<long>(0, 0);
            var merged = entryRangeMeta1.EntryLidRange
                .Merge(conversationRangeMeta1.ConversationLidRanges, (ce, co) => ce.IntersectWith(co).IsEmpty ? (int)(ce.Start - co.Start) : 0)
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
                && e.Kind == 0
                && e.LocalId >= idTileRange.Start
                && e.LocalId < idTileRange.End
                && !e.IsRemoved)
            .OrderBy(e => e.LocalId)
            .Select(e => e.LocalId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var previousEntryId = await dbContext.ChatEntries
            .Where(e => e.ChatId == chatId.Value
                && e.Kind == 0
                && e.LocalId < idTileRange.Start
                && !e.IsRemoved)
            .MaxAsync(e => (long?)e.LocalId, cancellationToken)
            .ConfigureAwait(false);

        var nextEntryId = await dbContext.ChatEntries
            .Where(e => e.ChatId == chatId.Value
                && e.Kind == 0
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

        var topReadPositions = dbReadPositionsStat.GetTopReadPositions();
        return new ReadPositionsStatBackend(chatId, dbReadPositionsStat.StartTrackingEntryLid, topReadPositions);
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
    public virtual async Task<ChatEntryAttachment[]> GetEntryAttachments(ChatEntryId entryId, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var idPrefix = DbChatEntryAttachment.IdPrefix(entryId);
        var dbAttachments = await dbContext.ChatEntryAttachments
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

        ChatEntryAttachment? WithMedia(ChatEntryAttachment attachment)
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

        if (query.LastLocalId == 0) {
            var dbEntries = await dbContext.ChatEntries
                .Where(x => x.ChatId == query.ChatId.Value
                    && x.Kind == 0
                    && x.Version >= query.MinVersion
                    && x.Version <= query.MaxVersion)
                .WhereIf(x => x.HasAttachments, query.RequireAttachments)
                .OrderBy(x => x.Version)
                .ThenBy(x => x.LocalId)
                .Take(query.Limit)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            return dbEntries.Select(x => x.ToModel()).ToArray();
        }

        var part1 = await dbContext.ChatEntries
            .Where(x => x.ChatId == query.ChatId.Value
                && x.Kind == 0
                && x.Version == query.MinVersion
                && x.LocalId > query.LastLocalId)
            .WhereIf(x => x.HasAttachments, query.RequireAttachments)
            .OrderBy(x => x.Version)
            .ThenBy(x => x.LocalId)
            .Take(query.Limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (part1.Count >= query.Limit)
            return part1.Select(x => x.ToModel()).ToArray();

        var part2 = await dbContext.ChatEntries
            .Where(x => x.ChatId == query.ChatId.Value
                && x.Kind == 0
                && x.Version > query.MinVersion
                && x.Version <= query.MaxVersion)
            .WhereIf(x => x.HasAttachments, query.RequireAttachments)
            .OrderBy(x => x.Version)
            .ThenBy(x => x.LocalId)
            .Take(query.Limit - part1.Count)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var result = new List<ChatEntry>(part1.Count + part2.Count);
        result.AddRange(part1.Select(x => x.ToModel()));
        result.AddRange(part2.Select(x => x.ToModel()));
        return result.ToArray();
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
                && x.Kind == 0
                && x.LocalId > minLocalIdExclusive)
            .OrderBy(x => x.LocalId)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var allAttachments = await GetAttachments(dbEntries, cancellationToken).ConfigureAwait(false);
        return dbEntries
            .Select(x => {
                var entryId = ChatEntryId.Parse(x.Id);
                var entryAttachments = allAttachments[entryId];
                return x.ToModel(entryAttachments);
            })
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

        var dbChat = chatId is null
            ? null
            : await dbContext.Chats.ForUpdate()
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
                    else if (Constants.Chat.SystemTags.Welcome == update.SystemTag)
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

            if (update.IsSummarized.IsNone || !update.IsSummarized.Value.HasValue) {
                var unsupportedSystemChats = new HashSet<Symbol> {
                    Constants.Chat.SystemTags.Welcome,
                    Constants.Chat.SystemTags.Notes,
                    Constants.Chat.SystemTags.Bot,
                };
                if (!update.SystemTag.HasValue || !unsupportedSystemChats.Contains(update.SystemTag.Value))
                    update = update with { IsSummarized = true }; // Enable summarization by default for new chats
            }

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

            }
            else if (chatId.Kind == ChatKind.Thread) {
                ownerId.Require("Command.OwnerId");
                var threadChatId = (ThreadChatId)chatId;
                // Threads carry no roles of their own; just ensure the creator is a member of the parent chat.
                await AuthorsBackend
                    .GetByUserId(threadChatId.GetOutermostParent(), ownerId, RequestedAuthorKind.Full, cancellationToken)
                    .Require()
                    .ConfigureAwait(false);
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
                if (Constants.Chat.SystemTags.Welcome == dbChat.SystemTag
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

            if (Constants.Chat.SystemTags.Welcome == dbChat.SystemTag)
                throw StandardError.Constraint("It's prohibited to remove 'Welcome' chat.");

            await RemoveMedia(dbChat.MediaId, cancellationToken).ConfigureAwait(false);
            var attachmentMediaIds = await dbContext.ChatEntries
                .Where(ce => ce.ChatId == chatId.Value && ce.HasAttachments)
                .Join(dbContext.ChatEntryAttachments, ce => ce.Id, ea => ea.EntryId, (_, ea) => ea.MediaId)
                .Distinct()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (var mediaSid in attachmentMediaIds) {
                var mediaId = MediaId.Parse(mediaSid);
                if (mediaId.Scope != chatId.Value)
                    continue; // NOTE(DF): Do not remove media from current chat scope. Forwarded messages can contain media from another chat.

                await RemoveMedia(mediaId, cancellationToken).ConfigureAwait(false);
            }
            // Remove attachments
            await dbContext.ChatEntries
                .Where(ce => ce.ChatId == chatId.Value && ce.HasAttachments)
                .Join(dbContext.ChatEntryAttachments, ce => ce.Id, ea => ea.EntryId, (_, ea) => ea)
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
                .Join(dbContext.Mentions.Where(m => m.ChatId == chatId.Value), ce => ce.LocalId, rs => rs.EntryLid, (_, rs) => rs)
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
        var expectedVersion = command.ExpectedVersion;
        var context = CommandContext.GetCurrent();
        const string boundToThreadHasChangedKey = "boundToThreadHasChanged";

        if (Invalidation.IsActive) {
            var invChatEntry = context.Operation.Items.KeylessGet<ChatEntry>();
            var invBoundToThreadHasChanged = context.Operation.Items.Get<bool>(boundToThreadHasChangedKey);
            var previousEntryId = context.Operation.Items.Get<long>(nameof(ChatEntryRangeMeta.PreviousEntryLid));
            var nextEntryId = context.Operation.Items.Get<long>(nameof(ChatEntryRangeMeta.NextEntryLid));
            if (invChatEntry != null) {
                InvalidateTiles(chatId, invChatEntry.LocalId, changeKind, invBoundToThreadHasChanged);

                var entryTile = IdTileStack.LastLayer.GetTile(invChatEntry.LocalId);
                _ = GetEntryRangeMeta(chatId, entryTile.Range.Start, default);

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
                var createdChatEntryId = context.Operation.Items.Get<ChatEntryId>(CreatedChatEntryId);
                if (createdChatEntryId is not null && previousEntryId == 0)
                    _ = GetMinLid(createdChatEntryId.ChatId, default);
                _ = GetMaxLid(chatId, true, default);
                _ = GetMaxLid(chatId, false, default);
                break;
            case ChangeKind.Update when invBoundToThreadHasChanged:
                _ = GetMaxLid(chatId, true, default);
                _ = GetMaxLid(chatId, false, default);
                break;
            case ChangeKind.Remove:
                _ = GetMaxLid(chatId, false, default);
                break;
            }
            return null!;
        }

        change.RequireValid();
        ChatEntry entry;
        ChatEntry? oldEntry;
        bool boundToThreadHasChanged = false;
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
                var localId = await DbNextLocalId(dbContext, chatId, cancellationToken)
                    .ConfigureAwait(false);
                chatEntryId = ChatEntryId.New(chatId, localId);
                entry = ChatEntry.NewEmpty(chatEntryId, update?.Kind ?? ChatEntryKind.Text) with {
                    Version = VersionGenerator.NextVersion(),
                    BeginsAt = Clocks.SystemClock.Now,
                };
                entry = ApplyDiff(entry, update, false);
                entry = await PrepareTextEntryForSave(entry, oldEntry, cancellationToken)
                    .ConfigureAwait(false);
                await EnforceNonContactPeerMessageLimit(dbContext, chatId, entry.AuthorId, cancellationToken)
                    .ConfigureAwait(false);
                dbEntry = new DbChatEntry(entry) {
                    HasAttachments = entry.Attachments.Length > 0,
                };
                dbContext.Add(dbEntry);
                context.Operation.Items.Set(CreatedChatEntryId, chatEntryId);
                await StorePreviousAndNextEntryIds(localId).ConfigureAwait(false);
            }
            else if (change.IsUpdate(out update)) {
                dbEntry.RequireVersion(expectedVersion);
                if (dbEntry.IsRemoved && update.IsRemoved == true)
                    throw StandardError.Constraint("Removed chat entries cannot be modified.");

                var existingChatEntry = dbEntry.ToModel();
                entry = ApplyDiff(existingChatEntry, update, true) with {
                    Version = VersionGenerator.NextVersion(dbEntry.Version),
                };
                entry = await PrepareTextEntryForSave(entry, oldEntry, cancellationToken).ConfigureAwait(false);
                var hasAttachments = update.Attachments is { Length: > 0 } || dbEntry.HasAttachments;
                dbEntry.UpdateFrom(entry);
                dbEntry.HasAttachments = hasAttachments;
                boundToThreadHasChanged = existingChatEntry.IsThread ^ entry.IsThread
                    || existingChatEntry.IsThreadStart ^ entry.IsThreadStart;
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
                    await StorePreviousAndNextEntryIds(localId).ConfigureAwait(false);
                }
            }
            else
                throw StandardError.Internal("Invalid ChatEntryDiff state.");

            context.Operation.Items.KeylessSet(entry);
            context.Operation.Items.Set(boundToThreadHasChangedKey, boundToThreadHasChanged);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            entry = dbEntry.ToModel().WithPopulatedValues(entry);
        }

        if (chatId is PlaceChatId { IsRoot: false })
            await EnsurePlaceChatAuthorExists(entry.AuthorId).ConfigureAwait(false);
        if (changeKind == ChangeKind.Remove) {
            // Clean up associated Media record when removing an entry with audio
            if (entry.Audio?.MediaId is { } removedMediaId && !removedMediaId.Value.IsNullOrEmpty()) {
                await RemoveMedia(removedMediaId, cancellationToken).ConfigureAwait(false);
            }
            await EnqueueChangedEvent().ConfigureAwait(false);
            return entry;
        }

        if (entry.IsContentStreaming)
            return entry;

        // Clean up Media when audio is stripped from an entry during update
        if (changeKind == ChangeKind.Update && oldEntry is not null) {
            var oldMediaSid = oldEntry.Audio?.MediaId?.Value;
            var newMediaSid = entry.Audio?.MediaId?.Value;
            if (!oldMediaSid.IsNullOrEmpty()
                && oldMediaSid != newMediaSid
                && MediaId.TryParse(oldMediaSid, out var strippedMediaId)) {
                await RemoveMedia(strippedMediaId, cancellationToken).ConfigureAwait(false);
            }
        }

        if (changeKind == ChangeKind.Create)
            AppMeters.MessageCount.Add(1);

        ChatEntryAttachment[]? attachmentsProto = null;
        if (change.IsCreate(out var create) && create.Attachments is { Length: > 0 } attachments1)
            attachmentsProto = attachments1;
        if (change.IsUpdate(out var update1) && update1.Attachments is { Length: > 0 } attachments2)
            attachmentsProto = attachments2;

        if (attachmentsProto is not null) {
            if (change.Kind is ChangeKind.Update) {
                var removeAttachmentsCmd = new ChatsBackend_RemoveAttachments(chatEntryId);
                await Commander.Call(removeAttachmentsCmd, cancellationToken).ConfigureAwait(false);
            }
            var newAttachments = attachmentsProto
                .Select((x, i) => new ChatEntryAttachment {
                    EntryId = chatEntryId,
                    Index = i,
                    MediaId = x.MediaId,
                    ThumbnailMediaId = x.ThumbnailMediaId,
                })
                .ToArray();
            var createAttachmentsCmd = new ChatsBackend_CreateAttachments(newAttachments);
            var createdAttachments = await Commander.Call(createAttachmentsCmd, cancellationToken).ConfigureAwait(false);
            entry = entry with { Attachments = createdAttachments };
        }

        // Let's enqueue the ChatEntryChangedEvent
        await EnqueueChangedEvent().ConfigureAwait(false);
        return entry;

        ChatEntry ApplyDiff(ChatEntry originalEntry, ChatEntryDiff? diff, bool isUpdate)
        {
            var oldAuthorId = originalEntry.AuthorId;
            var newEntry = (ChatEntry)DiffEngine.DynamicPatch(originalEntry, diff)! with {
                Version = VersionGenerator.NextVersion(originalEntry.Version),
            };
            if (newEntry.Id != originalEntry.Id)
                throw StandardError.Constraint("Chat Entry Id cannot be changed.");

            // Validation
            if (isUpdate) {
                if (newEntry.AuthorId != oldAuthorId)
                    throw StandardError.Unauthorized("You can edit only your own messages.");
                if (diff?.Content != null && newEntry.IsContentStreaming)
                    throw StandardError.Constraint("Only text messages can be edited.");
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
            context.Operation.AddEvent(new ChatEntryChangedEvent(entry, author!, changeKind, oldEntry));
        }

        async Task StorePreviousAndNextEntryIds(long localEntryLid)
        {
            var previousEntryId = await dbContext.ChatEntries
                .Where(c => c.ChatId == chatId.Value && c.Kind == 0 && !c.IsRemoved && c.LocalId < localEntryLid)
                .OrderByDescending(c => c.LocalId)
                .Select(c => c.LocalId)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            var nextEntryId = await dbContext.ChatEntries
                .Where(c => c.ChatId == chatId.Value && c.Kind == 0 && !c.IsRemoved && c.LocalId > localEntryLid)
                .OrderBy(c => c.LocalId)
                .Select(c => c.LocalId)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (previousEntryId != 0)
                context.Operation.Items.Set(nameof(ChatEntryRangeMeta.PreviousEntryLid), previousEntryId);
            if (nextEntryId != 0)
                context.Operation.Items.Set(nameof(ChatEntryRangeMeta.NextEntryLid), nextEntryId);
        }
    }

    // [CommandHandler]
    public virtual async Task<ChatEntryAttachment[]> OnCreateAttachments(
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
            InvalidateTiles(entryId.ChatId, entryId.LocalId, ChangeKind.Update, false);
            return default!;
        }

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbAttachments = new List<DbChatEntryAttachment>();
        foreach (var attachment in attachments) {
            var dbChatEntry = await dbContext.ChatEntries.Get(entryId.Value, cancellationToken)
                .Require()
                .ConfigureAwait(false);
            if (dbChatEntry.IsRemoved)
                throw StandardError.Constraint("Removed chat entries cannot be modified.");

            var dbAttachment = new DbChatEntryAttachment(attachment with {
                Version = VersionGenerator.NextVersion(),
            });
            dbContext.Add(dbAttachment);
            dbAttachments.Add(dbAttachment);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Keep the content index in sync even if this handler is ever called standalone
        // (without a follow-up entry update that would re-fire ResumeContentIndexing).
        if (Settings.IsChatContentItemIndexingEnabled)
            await FlowHub.NewResumeEvent<ChatMediaIndexingFlow>(entryId.ChatId.Value)
                .WithDelay(TimeSpan.FromSeconds(2))
                .Schedule(cancellationToken)
                .ConfigureAwait(false);

        return dbAttachments.Select(x => x.ToModel()).ToArray();
    }

    // [CommandHandler]
    public virtual async Task OnRemoveAttachments(
        ChatsBackend_RemoveAttachments command,
        CancellationToken cancellationToken)
    {
        var entryId = command.EntryId;

        if (Invalidation.IsActive) {
            _ = GetEntryAttachments(entryId, default);
            InvalidateTiles(entryId.ChatId, entryId.LocalId, ChangeKind.Update, false);
            return;
        }

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var idPrefix = DbChatEntryAttachment.IdPrefix(entryId);
        await dbContext.ChatEntryAttachments
            .Where(x => x.Id.StartsWith(idPrefix))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        // Keep the content index in sync even if this handler is ever called standalone
        // (without a follow-up entry update that would re-fire ResumeContentIndexing).
        // When it's called as part of an Update, the per-FlowId lock just serializes
        // the two resumes; the second one runs an empty no-op pass.
        if (Settings.IsChatContentItemIndexingEnabled)
            await FlowHub.NewResumeEvent<ChatMediaIndexingFlow>(entryId.ChatId.Value)
                .WithDelay(TimeSpan.FromSeconds(2))
                .Schedule(cancellationToken)
                .ConfigureAwait(false);
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
            var invChats = context.Operation.Items.KeylessGet<Dictionary<string, long>>();
            if (invChats == null)
                return;

            var tileSize = Constants.Chat.ServerIdTileStack.MinTileSize;
            foreach (var chatEntryPair in invChats) {
                var chatId = ChatId.Parse(chatEntryPair.Key);
                var entryId = chatEntryPair.Value;
                InvalidateTiles(chatId, entryId, ChangeKind.Remove, false);
                InvalidateTiles(chatId, entryId - tileSize, ChangeKind.Remove, false);
                InvalidateTiles(chatId, entryId - tileSize*2, ChangeKind.Remove, false);
                InvalidateTiles(chatId, entryId - tileSize*3, ChangeKind.Remove, false);
                InvalidateTiles(chatId, entryId - tileSize*4, ChangeKind.Remove, false);
                _ = GetEntryAttachments(ChatEntryId.New(chatId, entryId), default);
            }
            return;
        }

        var chatEntriesToInvalidate = new Dictionary<string, long>();
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
                .Join(dbContext.ChatEntryAttachments, ce => ce.Id, ea => ea.EntryId, (_, ea) => ea.MediaId)
                .Distinct()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (var mediaId in attachmentMediaIds)
                await RemoveMedia(mediaId, cancellationToken).ConfigureAwait(false);

            // Remove attachments
            await dbContext.ChatEntries
                .Where(ce => ce.ChatId == chatId && ce.AuthorId == authorId && ce.HasAttachments)
                .Join(dbContext.ChatEntryAttachments, ce => ce.Id, ea => ea.EntryId, (_, ea) => ea)
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
                .Join(dbContext.Mentions.Where(m => m.ChatId == chatId), ce => ce.LocalId, rs => rs.EntryLid, (_, rs) => rs)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
            // Remove entry languages
            var chatEntryIdPrefix = ChatEntryId.Prefix(chatId);
            await dbContext.ChatEntries
                .Where(ce => ce.ChatId == chatId && ce.AuthorId == authorId)
                .Join(dbContext.ChatEntryLanguages.Where(m => m.Id.StartsWith(chatEntryIdPrefix)),
                    ce => ce.Id,
                    cel => cel.Id,
                    (_, cel) => cel)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
            // Remove translations
            await dbContext.ChatEntries
                .Where(ce => ce.ChatId == chatId && ce.AuthorId == authorId)
                .Join(dbContext.Translations.Where(m => m.Id.StartsWith(chatEntryIdPrefix)),
                    ce => ce.Id,
                    t => t.EntryId,
                    (_, t) => t)
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
        var entryLid = command.EntryLid;

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);
        await dbContext.ReadPositionsStats.Lock(chatId, cancellationToken).ConfigureAwait(false);
        var dbReadPositionsStat = await dbContext.ReadPositionsStats
            .FirstOrDefaultAsync(c => c.ChatId == chatId.Value, cancellationToken)
            .ConfigureAwait(false);

        var hasChanges = false;
        if (dbReadPositionsStat != null) {
            if (dbReadPositionsStat.StartTrackingEntryLid <= entryLid) {
                var readPositions = dbReadPositionsStat.GetTopReadPositions();
                var sameUserIndex = Array.FindIndex(readPositions, c => c.UserId == userId);
                if (sameUserIndex >= 0) { // There is a position of the same user
                    if (readPositions[sameUserIndex].EntryLid < entryLid) { // And its EntryLid is lower
                        readPositions[sameUserIndex] = new UserReadPosition(userId, entryLid);
                        hasChanges = true;
                    }
                }
                else { // There is no position of the same user
                    readPositions = readPositions.With(new UserReadPosition(userId, entryLid));
                    hasChanges = true;
                }
                if (hasChanges) {
                    Array.Sort(readPositions, UserReadPosition.Comparer);
                    var top1 = readPositions[0];
                    var top2 = readPositions.Length > 1 ? readPositions[1] : default;
                    dbReadPositionsStat.Version = VersionGenerator.NextVersion(dbReadPositionsStat.Version);
                    dbReadPositionsStat.Top1UserId = top1.UserId?.Value ?? "";
                    dbReadPositionsStat.Top1EntryLid = top1.EntryLid;
                    dbReadPositionsStat.Top2UserId = top2.UserId?.Value ?? "";
                    dbReadPositionsStat.Top2EntryLid = top2.EntryLid;
                }
            }
        }
        else {
            var idRange = await GetLidRange(chatId, false, cancellationToken).ConfigureAwait(false);
            var lastEntryId = idRange.End - 1; // Start tracking positions stat since this entry
            var shouldTrackPosition = entryLid >= lastEntryId;
            dbContext.Add(new DbReadPositionsStat() {
                ChatId = chatId.Value,
                Version = VersionGenerator.NextVersion(),
                StartTrackingEntryLid = lastEntryId,
                Top1UserId = shouldTrackPosition ? userId.Value : "",
                Top1EntryLid = shouldTrackPosition ? entryLid : 0,
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
    public virtual async Task OnNewAccountEvent(NewAccountEvent eventCommand, CancellationToken cancellationToken)
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
        AuthorFull? readAuthor;
        var retryPolicy = new RetryPolicy(5, RetryDelaySeq.Exp(0.25, 1));
        var tryIndex = 0;
        while (true) {
            readAuthor = await AuthorsBackend.Get(author.ChatId, author.Id, RequestedAuthorKind.Full, cancellationToken).ConfigureAwait(false);
            if (readAuthor?.Avatar != null)
                break;
            if (!retryPolicy.MustRetry(++tryIndex))
                throw StandardError.NotFound<Avatar>();

            var delay = retryPolicy.GetDelay(tryIndex);
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
        var authorId = readAuthor.IsAnonymous ? null : author.Id;
        var authorName = readAuthor.IsAnonymous ? "Someone" : readAuthor.Avatar.Name;
        if (authorName.IsNullOrEmpty())
            authorName = MentionMarkup.NotAvailableName;

        var entryId = ChatEntryId.New(author.ChatId, 0);
        var command = new ChatsBackend_ChangeEntry(
            entryId,
            null,
            Change.Create(new ChatEntryDiff {
                Kind = ChatEntryKind.MembersChanged,
                AuthorId = Bots.GetWalleId(author.ChatId),
                TargetAuthorId = authorId,
                TargetAuthorName = authorName,
                HasLeft = author.HasLeft,
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
            if (chat != null && Constants.Chat.SystemTags.Welcome == chat.SystemTag) {
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
            var startThreadEntryId = ChatEntryId.New(threadChatId, threadChatId.ThreadId);
            var chatEntry = await this.GetEntry(startThreadEntryId, cancellationToken).ConfigureAwait(false);
            if (chatEntry is not null && chatEntry.IsThreadStart) {
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
            await FlowHub.NewResumeEvent<ConversationSplitFlow>(chat.Id.Value)
                .Schedule(cancellationToken)
                .ConfigureAwait(false);
        return;

        bool NeedsSummarization()
            => chat.IsSummarized == true && oldChat?.IsSummarized != true;
    }

    // [EventHandler]
    public virtual async Task OnChatEntryChangedEvent(ChatEntryChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var (entry, _, kind, _) = eventCommand;
        if (kind == ChangeKind.Create)
            await FlowHub.NewResumeEvent<ChatEntryFixupFlow>(entry.ChatId.Value)
                .WithDelay(Constants.Chat.StreamingEntryFixupDelay + TimeSpan.FromSeconds(1))
                .Schedule(cancellationToken).ConfigureAwait(false);

        await ResumeContentIndexing(eventCommand, cancellationToken).ConfigureAwait(false);

        if (entry.IsContentStreaming)
            return; // Streaming entries are not summarized

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

            var endsAt = Moment.Max(entry.GetEndsAt(), Clocks.SystemClock.Now);
            await FlowHub
                .NewResumeEvent<ConversationSplitFlow>(chat.Id.Value)
                .WithDelay(endsAt + Settings.Summarization.ChatEntrySummarizationDelay, Settings.Summarization.ChatEntrySummarizationDelayQuanta)
                .Schedule(cancellationToken)
                .ConfigureAwait(false);
        }
    }

    // Protected methods

    protected void InvalidateTiles(
        ChatId chatId,
        long entryId,
        ChangeKind changeKind,
        bool boundToThreadHasChanged)
    {
        // Invalidate GetTile for chat tiles
        foreach (var idTile in IdTileStack.GetAllTiles(entryId)) {
            if (idTile.Layer.Smaller != null)
                continue;

            // Larger tiles are composed out of smaller tiles,
            // so we have to invalidate just the smallest one.
            // And the tile with includeRemoved == false is based on
            // a tile with includeRemoved == true, so we have to invalidate
            // just this tile.
            _ = GetTile(chatId, idTile.Range, true, default);
        }

        if (changeKind is ChangeKind.Create or ChangeKind.Remove || boundToThreadHasChanged) {
            // Invalidate GetEntryRangeMeta
            var tile = IdTileStack.LastLayer.GetTile(entryId);
            _ = GetEntryRangeMeta(chatId, tile.Start, default);
        }
    }

    private Task ResumeContentIndexing(ChatEntryChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        if (!Settings.IsChatContentItemIndexingEnabled)
            return Task.CompletedTask;

        var (entry, _, kind, oldEntry) = eventCommand;
        if (entry.IsSystemEntry)
            return Task.CompletedTask;

        var hasOrHadLinkPreviews = entry.LinkPreviewIds.Length > 0
            || oldEntry?.LinkPreviewIds.Length > 0;
        // Remove: ChatEntry doesn't carry HasAttachments, and oldEntry is loaded without
        // attachments, so we don't know if the entry being removed had any. Schedule
        // defensively — the media flow's delete-by-entryId is a no-op when no rows match.
        // Update never empties attachments (only replaces with a non-empty list), so the
        // current entry.Attachments alone is sufficient there.
        var hasOrHadAttachments = entry.Attachments.Length > 0
            || kind == ChangeKind.Remove;

        if (!hasOrHadLinkPreviews && !hasOrHadAttachments)
            return Task.CompletedTask;

        var chatSid = entry.ChatId.Value;
        var tasks = new List<Task>(2);
        if (hasOrHadLinkPreviews)
            tasks.Add(FlowHub.NewResumeEvent<ChatEntryContentIndexingFlow>(chatSid)
                .WithDelay(TimeSpan.FromSeconds(2))
                .Schedule(cancellationToken));
        if (hasOrHadAttachments)
            tasks.Add(FlowHub.NewResumeEvent<ChatMediaIndexingFlow>(chatSid)
                .WithDelay(TimeSpan.FromSeconds(2))
                .Schedule(cancellationToken));
        return Task.WhenAll(tasks);
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

    internal Task<long> DbNextLocalId(
        ChatDbContext dbContext,
        ChatId chatId,
        CancellationToken cancellationToken)
        => DbChatEntryIdGenerator.Next(dbContext, chatId.Value, cancellationToken);

    private async Task<ChatEntry> PrepareTextEntryForSave(ChatEntry entry, ChatEntry? existing, CancellationToken cancellationToken)
    {
        if (entry.IsSystemEntry || entry.IsContentStreaming)
            return entry;

        var wasContentChanged = entry.Content != (existing?.Content ?? "");
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

        var emails = account.Identities.GetEmails();
        var hasTeamEmail = emails.Any(e => e.EndsWith(Constants.Team.EmailSuffix));
        if (!hasTeamEmail)
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

    private async Task EnforceNonContactPeerMessageLimit(
        ChatDbContext dbContext,
        ChatId? chatId,
        AuthorId? authorId,
        CancellationToken cancellationToken)
    {
        if (chatId is not PeerChatId peerChatId || authorId is null || authorId.Value.IsNullOrEmpty())
            return;

        var author = await AuthorsBackend
            .Get(chatId, authorId, RequestedAuthorKind.Full, cancellationToken)
            .ConfigureAwait(false);
        if (author is null)
            return;

        var senderUserId = author.UserId;
        var peerUserId = peerChatId.AnotherUserIdOrNull(senderUserId);
        if (peerUserId.IsGuestOrNull())
            return;

        var peerContactId = ContactId.NewUser(peerUserId, senderUserId);
        var peerContact = await ContactsBackend.Get(peerUserId, peerContactId, cancellationToken).ConfigureAwait(false);
        if (peerContact.IsRegular)
            return;

        // Serialize concurrent cap checks per (chat, author) so two racing creates can't both pass
        await dbContext.ChatEntries.Lock(chatId.Value, authorId.Value, cancellationToken).ConfigureAwait(false);

        var limit = Constants.Chat.NonContactPeerMessageLimit;
        var sAuthorId = authorId.Value;
        var sChatId = chatId.Value;
        var authorEntryCount = await dbContext.ChatEntries
            .Where(e => e.ChatId == sChatId && e.AuthorId == sAuthorId && e.Kind == 0)
            .Take(limit)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);
        if (authorEntryCount >= limit)
            throw StandardError.Constraint(
                $"You can send up to {limit} messages until this user adds you to their contacts or replies.");
    }

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

        var permissions = (ChatPermissions.Write | ChatPermissions.SeeMembers | ChatPermissions.Join | ChatPermissions.EditProperties).AddImplied();

        // Strip stream + upload capabilities if the recipient hasn't explicitly stored
        // the caller's contact. A recipient reply stores this contact automatically.
        // The message-count cap on creates is enforced separately in Chats.OnUpsertEntry.
        var peerUserId = otherUserId!;
        var peerContactId = ContactId.NewUser(peerUserId, account.Id);
        var peerContact = await ContactsBackend.Get(peerUserId, peerContactId, cancellationToken).ConfigureAwait(false);
        if (!peerContact.IsRegular)
            permissions &= ~(ChatPermissions.Upload
                | ChatPermissions.WriteAudio
                | ChatPermissions.WriteVideo
                | ChatPermissions.ReadAudio
                | ChatPermissions.ReadVideo);

        return new(chatId, author, account, permissions);
    }

    private async Task<Chat> EnsureExists(PeerChatId peerChatId, CancellationToken cancellationToken)
    {
        var chat = await Get(peerChatId, cancellationToken).ConfigureAwait(false);
        if (chat.HasVersion())
            return chat;

        var command = new ChatsBackend_Change(peerChatId, null, new() { Create = new ChatDiff() });
        chat = await Commander.Call(command, true, cancellationToken).ConfigureAwait(false);
        return chat;
    }

    private async Task<bool> HasActivatedInvite(UserId userId, ChatId chatId, CancellationToken cancellationToken)
    {
        var settings = await ServerKvasBackend.ForUser(userId)
            .Get<ChatInviteSettings>(ChatInviteSettings.GetKey(chatId), cancellationToken)
            .ConfigureAwait(false);
        if (settings is not { ActivationKey: { Length: > 0 } activationKey })
            return false;

        return await InvitesBackend.IsValid(activationKey, cancellationToken).ConfigureAwait(false);
    }

    private Task<ILookup<ChatEntryId, ChatEntryAttachment>> GetAttachments(IEnumerable<DbChatEntry> dbEntries, CancellationToken cancellationToken)
    {
        var entryIdsWithAttachments = dbEntries.Where(x => x.HasAttachments)
            .Select(x => ChatEntryId.Parse(x.Id))
            .ToList();

        return entryIdsWithAttachments.Count > 0
            ? GetAttachmentsBulk()
            : EmptyAttachmentsTask;

        async Task<ILookup<ChatEntryId, ChatEntryAttachment>> GetAttachmentsBulk() {
            var attachments = await entryIdsWithAttachments
                .Select(x => GetEntryAttachments(x, cancellationToken))
                .Collect(cancellationToken)
                .ConfigureAwait(false);
            return attachments.SelectMany(x => x).ToLookup(x => x.EntryId);
        }
    }

    private Task<IReadOnlyDictionary<string, ChatEntryAudio>> GetAudioMap(
        IEnumerable<DbChatEntry> dbEntries,
        CancellationToken cancellationToken)
    {
        var mediaIds = dbEntries
            .Select(e => e.AudioId)
            .Where(id => !id.IsNullOrEmpty() && MediaId.TryParse(id, out _))
            .Select(id => MediaId.Parse(id!))
            .Distinct()
            .ToList();
        return mediaIds.Count > 0
            ? ResolveAudio()
            : EmptyAudioMapTask;

        async Task<IReadOnlyDictionary<string, ChatEntryAudio>> ResolveAudio()
        {
            var mediaList = await mediaIds
                .Select(mid => MediaBackend.Get(mid, cancellationToken))
                .Collect(cancellationToken)
                .ConfigureAwait(false);
            var map = new Dictionary<string, ChatEntryAudio>();
            foreach (var media in mediaList) {
                if (media is null)
                    continue;

                var audio = new ChatEntryAudio {
                    MediaId = media.Id,
                    BlobId = media.BlobId,
                    BeginsAt = media.BeginsAt,
                    EndsAt = media.EndsAt != default ? media.EndsAt : null,
                    ContentEndsAt = media.ContentEndsAt != default ? media.ContentEndsAt : null,
                    ClientSideBeginsAt = media.ClientSideBeginsAt != default ? media.ClientSideBeginsAt : null,
                };
                map[media.Id.Value] = audio;
            }
            return map;
        }
    }

    private Task RemoveMedia(string mediaSid, CancellationToken cancellationToken)
        => !mediaSid.IsNullOrEmpty() ? RemoveMedia(MediaId.Parse(mediaSid), cancellationToken) : Task.CompletedTask;

    private async Task RemoveMedia(MediaId mediaId, CancellationToken cancellationToken)
    {
        var removeCommand = new MediaBackend_Change(
            mediaId,
            null,
            new Change<MediaFull> { Remove = true });
        await Commander.Call(removeCommand, true, cancellationToken).ConfigureAwait(false);
    }
}
