using System.Diagnostics.Metrics;
using ActualChat.Diagnostics;
using CommunityToolkit.HighPerformance;

namespace ActualChat.UI.Blazor.App.Services;

public record ChatItems(IReadOnlyList<ChatMessage> Items, bool HasBefore, bool HasAfter)
{
    public static readonly ChatItems Empty = new([], false, false);
}

public sealed record ConversationViewState(
    bool ShowConversations,
    IImmutableSet<ConversationId> ExpandedConversations,
    Range<long> HiddenLiveTailRange,
    ConversationId? LiveBlockConversationId,
    Range<long> LiveFoldRange,
    ConversationId? MaterializedBlockId)
{
    public bool Equals(ConversationViewState? other)
    {
        // Hand-written because this record is a ChatUI.GetTile compute-method argument - it IS the tile
        // cache key. ExpandedConversations is rebuilt from scratch on every GetChatItems call, and the
        // compiler's synthesized comparison falls back to reference equality for it, so every rebuild
        // would re-key (and thereby discard) every cached tile.
        if (other is null)
            return false;

        if (ReferenceEquals(this, other))
            return true;

        return ShowConversations == other.ShowConversations
            && HiddenLiveTailRange == other.HiddenLiveTailRange
            && LiveBlockConversationId == other.LiveBlockConversationId
            && LiveFoldRange == other.LiveFoldRange
            && MaterializedBlockId == other.MaterializedBlockId
            && ExpandedConversations.SetEquals(other.ExpandedConversations);
    }

    public ConversationViewState NarrowTo(Range<long> scope)
    {
        // Both ranges reach ChatUI.GetTile only as idRangesToSkip entries, so one that can't reach the
        // tile can never match and dropping it is equivalent - and this record is the tile cache key
        // shared by every tile, so otherwise one scroll step inside a live block re-keys them all.
        // ExpandedConversations stays: a conversation may start before the tile and extend into it.
        var hiddenLiveTailRange = HiddenLiveTailRange.IntersectWith(scope).IsEmpty
            ? default
            : HiddenLiveTailRange;
        var liveFoldRange = LiveFoldRange.IntersectWith(scope).IsEmpty ? default : LiveFoldRange;
        if (hiddenLiveTailRange == HiddenLiveTailRange && liveFoldRange == LiveFoldRange)
            return this;

        return this with {
            HiddenLiveTailRange = hiddenLiveTailRange,
            LiveFoldRange = liveFoldRange,
        };
    }

    public override int GetHashCode()
    {
        var hash = HashCode.Combine(ShowConversations,
            HiddenLiveTailRange,
            LiveBlockConversationId,
            LiveFoldRange,
            MaterializedBlockId,
            ExpandedConversations.Count);
        foreach (var conversationId in ExpandedConversations)
            hash ^= conversationId.GetHashCode(); // XOR - the set has no order
        return hash;
    }
}

public partial class ChatUI
{
    private const int OwnAudioEntryScanDepth = 10;
    private static readonly TimeSpan StreamingEntryGap = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan BlockStartTimeGap = TimeSpan.FromSeconds(120);
    // Server-side only: it reaches Google Managed Prometheus via the server's OTel pipeline, and MAUI
    // registers no meter at all - a device reports these numbers through the slow-build log below.
    private static readonly Histogram<long> GetChatItemsDuration = AppUIInstruments.Meter
        .CreateHistogram<long>("actualchat.chat_ui.get_chat_items.duration", "ms",
            "GetChatItems wall-clock duration, split by phase");

    public static readonly TileStack<long> IdTileStack = Constants.Chat.ViewIdTileStack;
    public static readonly TileStack<long> ServerIdTileStack = Constants.Chat.ServerIdTileStack;
    public static readonly int SecondTileSize = (int)IdTileStack.LastLayer.TileSize; // 20
    // Mirrors VirtualList.ExpandMultiplier = 2, so the load zone is 1 viewport + 2 x 2 viewports.
    private const double VirtualListLoadZoneSize = 5;
    // VirtualListStatistics.DefaultItemSize (48) is tuned for uniform lists; measured chat rows average
    // ~82px. This sits deliberately below that: under-estimating row height over-fetches a little, while
    // over-estimating it would under-fill the load zone and cost the follow-up query we're avoiding.
    private const double AssumedItemSize = 64;
    // Sits below the chronic 150-400ms RTT-bound rebuild band whose share grows through a live session:
    // on a device the log this triggers is the only channel that carries the phase split, and at 500ms
    // it never fired - a whole session of 150-450ms rebuilds produced zero samples.
    private const long SlowBuildMs = 250;

    private volatile ChatEntry? _audioRecordingEntry;

    private IImmutableSet<ConversationId> LastConversationExpansionOverrides { get; set; } =
        ImmutableHashSet<ConversationId>.Empty;

    // Remembers each conversation's IsExpandedByDefault once its tile has been seen, so a conversation
    // keeps its expanded/collapsed state when the VirtualList unloads and reloads its tile mid-session -
    // otherwise defaultExpanded (derived only from currently-loaded tiles) would drop it and flip an
    // expanded conversation to collapsed, jumping the content height and the scroll position. Concurrent
    // because GetChatItemsInternal (a compute method) runs re-entrantly - a plain field read-modify-write
    // would let a stale snapshot clobber accumulated entries.
    private readonly ConcurrentDictionary<ConversationId, bool> _knownConversationDefaultExpanded = new();

    // Serve-stale caches for the meta phase of GetChatItemsInternal: per chat, the last fresh range-meta
    // list, conversation tiles, and load zone. A rebuild whose meta is being refetched (typically because
    // a new entry invalidated it) renders from these instead of waiting a round-trip; UseIfReady
    // guarantees a follow-up rebuild from the fresh values.
    private readonly ConcurrentDictionary<ChatId, List<ChatRangeMeta>> _lastChatRangeMetas = new();
    private readonly ConcurrentDictionary<ChatId, Conversation[][]> _lastConversationTiles = new();
    private readonly ConcurrentDictionary<ChatId, (List<Range<long>> IdTiles, bool ShowConversations)>
        _lastLoadZones = new();

    public int HalfLoadLimit => BrowserInfo.IsMobile ? SecondTileSize : SecondTileSize * 2; // 20 for mobile
    public int LoadLimit => BrowserInfo.IsMobile ? SecondTileSize * 2 : SecondTileSize * 4; // 40 for mobile

    // The VirtualList loads viewport +/- ExpandMultiplier x viewport and sizes its own requests as
    // loadZone / itemSize. The very first request is issued before the list exists, so it carries no
    // ExpectedCount and we have to mirror that formula here - otherwise a fixed row count means ~4
    // viewports where messages are long and ~16 where they're short, which is what made switching into
    // a chat mid-history slow. A collapsed conversation costs one row here regardless of its lid span,
    // because TryGetIdTilesToLoad walks it as a single [start, start+1) range.
    public int InitialLoadLimit {
        get {
            var windowHeight = BrowserInfo.WindowHeight.Value;
            if (windowHeight <= 0)
                return LoadLimit; // Height not reported yet - keep the old behaviour

            var rows = (int)Math.Ceiling(VirtualListLoadZoneSize * windowHeight / AssumedItemSize);
            return Math.Clamp(rows, SecondTileSize * 2, SecondTileSize * 8);
        }
    }

    public Task<ChatItems> GetChatItems(
        ChatId chatId,
        ChatDataQuery dataQuery,
        long shownReadyEntryLid,
        CancellationToken cancellationToken)
        => GetChatItemsInternal(chatId, dataQuery, shownReadyEntryLid, false, cancellationToken);

    [ComputeMethod]
    public virtual ValueTask<ChatEntry?> GetEntry(
        ChatEntryId id,
        CancellationToken cancellationToken = default)
        => Chats.GetEntry(Session, id, cancellationToken);

    // Private methods

    private async Task<ChatItems> GetChatItemsInternal(
        ChatId chatId,
        ChatDataQuery dataQuery,
        long shownReadyEntryLid,
        bool isPrefetch,
        CancellationToken cancellationToken)
    {
        // DebugLog?.LogDebug("GetTiles: {ChatId} {IdRange} {ShownReadyEntryLid}", chatId, dataQuery, shownReadyEntryLid);
        var startedAt = CpuTimestamp.Now;
        // Captured to detect a background stretch inside this build: GetData gates on visibility before
        // calling us, but the app can background AFTER that gate, and the in-flight RPCs below then don't
        // complete until it returns - which would land the whole background period in the live phase.
        var cIsVisible = isPrefetch ? null : BrowserInfo.IsVisible.Computed;
        // Each of these is a separate RPC round-trip on a remote client, and none of them depends on
        // another, so they're all issued before the first await. Awaiting them in sequence cost ~4 x RTT
        // per rebuild - a full second on a 200ms+ link, which is invisible on Blazor Server.
        var chatTask = Chats.Get(Session, chatId, cancellationToken);
        var liveConversationTask = Hub.LiveSessionUI.GetConversation(chatId, cancellationToken);
        var rawLiveTask = Hub.LiveSessionUI.GetBlockSnapshot(chatId, cancellationToken);
        var blockStateTask = Hub.LiveBlockUI.GetBlockState(chatId, cancellationToken);
        var metaIdTiles = ServerIdTileStack.LastLayer.GetCoveringTiles(dataQuery.ExistingLidRange.Expand(LoadLimit))
            .Where(t => t.Start >= 0)
            .ToList();
        var chatRangeMetaListTask = metaIdTiles
            .Select(metaIdTile
                => Chats.GetChatRangeMeta(Session, chatId, metaIdTile.Range.Start, cancellationToken))
            .Collect(cancellationToken);
        // Issued here rather than under the showConversations check below, which needs chat + liveConversation
        // and so could only run a round later. Summarization is on by default, so showConversations is nearly
        // always true; for the rare chat without conversations this tile set comes back empty and then only
        // invalidates if summarization is turned on - so the unconditional dependency costs ~nothing.
        var conversationTilesTask = metaIdTiles
            .Select(t => Conversations.GetTile(Session, chatId, t.Range, cancellationToken))
            .Collect(cancellationToken);
        Task<Range<long>> chatLidRangeTask;
        using (Computed.BeginIsolation())
            chatLidRangeTask = Chats.GetIdRange(Session, chatId, cancellationToken);

        // A rebuild whose range meta was invalidated (typically by a new entry) almost always finds its
        // load-zone tiles invalidated too, and would otherwise pay those round-trips back to back. Warm
        // the tiles from the previous build's zone while the meta fetch is in flight - isolated, so a
        // stale zone can't register dependencies; the dependency-registering pass below joins the same
        // in-flight computations.
        if (!isPrefetch && !chatRangeMetaListTask.IsCompleted
            && _lastLoadZones.TryGetValue(chatId, out var lastLoadZone)) {
            var (lastIdTiles, lastShowConversations) = lastLoadZone;
            using (Computed.BeginIsolation())
                _ = BackgroundTask.Run(
                    () => PrefetchLoadZone(chatId, lastIdTiles, lastShowConversations, cancellationToken),
                    Log, "Speculative load-zone prefetch failed.", cancellationToken);
        }

        var chat = await chatTask.ConfigureAwait(false);
        if (chat == null) {
            // Everything above is already in flight, so bailing out here would leave those tasks unobserved.
            _ = Task.WhenAll(
                    liveConversationTask, rawLiveTask, blockStateTask,
                    chatRangeMetaListTask, conversationTilesTask, chatLidRangeTask)
                .SilentAwait(false);
            return ChatItems.Empty;
        }

        // These are all in flight already, so each timestamp marks when that await stopped blocking -
        // the first long delta is the straggler, the rest complete immediately behind it.
        var chatAt = CpuTimestamp.Now;
        var liveConversation = await Hub.LiveSessionUI
            .UseConversationOrLastKnown(chatId, liveConversationTask)
            .ConfigureAwait(false);
        var conversationAt = CpuTimestamp.Now;
        var amInLiveConversation = liveConversation != null
            && await Hub.LiveSessionUI.AmIInLiveConversation(chatId, cancellationToken).ConfigureAwait(false);
        var amInLiveAt = CpuTimestamp.Now;
        var rawLive = await Hub.LiveSessionUI
            .UseSnapshotOrLastKnown(chatId, rawLiveTask)
            .ConfigureAwait(false);
        var snapshotAt = CpuTimestamp.Now;
        var blockState = await blockStateTask.ConfigureAwait(false);
        var overlay = blockState.Overlay;

        // The governed boundary replaces the raw fold end: it tracks the viewer's viewport top,
        // monotonically, so un-summarised rows above the viewport fold too (LiveBlockUI owns this).
        // No EndEntryLid cap here - FoldBoundaryLid is itself the monotonic max of real visible
        // message lids, so it never exceeds the loaded entries. RevealedBoundaryLid pins the
        // *effective* boundary below the monotonic one once the reader asks for more (§7), so
        // revealed rows stay visible even as the governor keeps advancing.
        var effectiveFoldBoundaryLid = Math.Min(blockState.FoldBoundaryLid, blockState.RevealedBoundaryLid);
        var liveFoldRange = rawLive is { IsLatched: true }
            && effectiveFoldBoundaryLid > rawLive.VisibleStartLid
                ? new Range<long>(rawLive.VisibleStartLid, effectiveFoldBoundaryLid)
                : default;

        ConversationId? liveBlockId;
        Range<long> liveBlockFoldRange;
        ConversationId? materializedBlockId = null;
        if (overlay != null) {
            liveBlockId = overlay.RenderId;
            liveBlockFoldRange = overlay.FoldRange;
            materializedBlockId = overlay.MaterializedId;
        }
        else {
            // The live block uses the same shell joined or not; only the tint + header affordance differ.
            liveBlockId = liveConversation?.Id;
            liveBlockFoldRange = liveFoldRange;
        }

        // A non-joined viewer sees the block's summary card only — never its live entries. The card
        // collapses [V, foldEnd) and this range hides everything from there on, so the two together
        // cover [V, ∞) gaplessly. Starting the hidden range at foldEnd (not EndEntryLid+1) is what
        // makes it gapless: a non-joined viewer never renders live entries, so the governed boundary
        // never gets a viewport signal and can trail EndEntryLid indefinitely - anchoring to
        // EndEntryLid would leak the entries in [foldEnd, EndEntryLid+1). Pre-first-summary the fold
        // range is empty, so hide from V onward. Hiding the wider range is harmless - a non-joined
        // viewer has no visible live entries to keep.
        var hiddenLiveTailRange = overlay != null
            ? overlay.HiddenTailRange
            : liveConversation is { } liveConv && !amInLiveConversation
                ? new Range<long>(
                    liveBlockFoldRange.IsEmpty
                        ? liveConv.Id.StartEntryLid
                        : Math.Min(liveConv.EndEntryLid + 1, liveBlockFoldRange.End),
                    long.MaxValue)
                : default;
        var liveAt = CpuTimestamp.Now;

        // Fresh by construction: the view awaited GetIdRange right before calling in, so this is a cache
        // hit even on the rebuild a new entry just triggered - which is what makes the tail synthesis in
        // UseRangeMetaOrLastKnown sound.
        var chatLidRange = await chatLidRangeTask.ConfigureAwait(false);
        var metaIdTilesRange = metaIdTiles.Count > 0
            ? new Range<long>(metaIdTiles[0].Range.Start, metaIdTiles[^1].Range.End)
            : default;
        var lastKnownRangeMetaList = UseRangeMetaOrLastKnown(
            chatId, chatRangeMetaListTask, metaIdTilesRange, chatLidRange, isPrefetch);
        var chatRangeMetaList = lastKnownRangeMetaList
            ?? (await chatRangeMetaListTask.ConfigureAwait(false))
                .OrderBy(m => m.LidRange.Start)
                .ThenByDescending(m => m.LidRange.Size()) // ChatRangeMeta can be overlapping, so we need to keep the largest
                .EnsureMonotonic(Comparer<ChatRangeMeta>.Create((a, b) => a.LidRange.Start.CompareTo(b.LidRange.Start)))
                .ToList();

        var showConversations = (chat.IsSummarized ?? false) || liveConversation != null;

        // Effective expansion = each conversation's IsExpandedByDefault flipped by a local override, so
        // expandedConversations below is the resolved "render expanded" set, not the raw override set.
        IImmutableSet<ConversationId> defaultExpanded = ImmutableHashSet<ConversationId>.Empty;
        IImmutableSet<ConversationId> expandedConversations = ImmutableHashSet<ConversationId>.Empty;
        // A one-build-stale value is harmless here: conversation tiles only fold into the persistent
        // expansion cache below, while the conversation ranges the layout uses come from the range meta.
        var conversationTiles = UseOrLastKnown(_lastConversationTiles, chatId, conversationTilesTask, isPrefetch)
            ?? await conversationTilesTask.ConfigureAwait(false);
        if (conversationTilesTask.IsCompletedSuccessfully)
            _lastConversationTiles[chatId] = conversationTilesTask.Result;
        if (showConversations) {
            // Fold the loaded tiles' IsExpandedByDefault into the persistent cache (loaded tiles are
            // authoritative), then resolve defaultExpanded from the cache - so a conversation whose tile
            // isn't currently loaded keeps its last-known state instead of silently collapsing.
            foreach (var c in conversationTiles.SelectMany(t => t))
                _knownConversationDefaultExpanded[c.Id] = c.IsExpandedByDefault;
            defaultExpanded = _knownConversationDefaultExpanded
                .Where(kv => kv.Value)
                .Select(kv => kv.Key)
                .ToImmutableHashSet();

            if (dataQuery.Navigation is { ShouldRestoreViewPosition: false, KeepConversationsCollapsed: false }) {
                var conversationRanges = chatRangeMetaList
                    .SelectMany(rm => rm.ConversationLidRanges)
                    .EnsureMonotonic()
                    .ToList();
                var navigateToId = dataQuery.Navigation.EntryLid;
                var index = conversationRanges.AsSpan().BinarySearch(r => r.Contains(navigateToId) || r.Start > navigateToId);
                if (index >= 0) {
                    var conversationRange = conversationRanges[index];
                    if (conversationRange.Contains(navigateToId)) {
                        // Ensure the navigated-to conversation is effective-expanded (flip its override if not).
                        var conversationId = ConversationId.New(chatId, conversationRange.Start);
                        var overrides0 = ConversationExpansionOverrides.Value;
                        var isExpanded = defaultExpanded.Contains(conversationId) ^ overrides0.Contains(conversationId);
                        if (!isExpanded) {
                            var newOverrides = overrides0.Contains(conversationId)
                                ? overrides0.Remove(conversationId)
                                : overrides0.Add(conversationId);
                            _conversationExpansionOverrides.Value = newOverrides;
                            LastConversationExpansionOverrides = newOverrides;
                        }
                    }
                }
            }

            var overrides = await ConversationExpansionOverrides.Use(cancellationToken).ConfigureAwait(false);
            // A prefetch is a speculative background load nothing renders, so it must not consume the
            // one-shot "just toggled" signal - the rebuild that does render would then never widen its
            // window to the toggled conversation, and the entries it just revealed would never load.
            if (!isPrefetch) {
                var changedOverrides = overrides.SymmetricExcept(LastConversationExpansionOverrides)
                    .OrderBy(c => c.StartEntryLid)
                    .ToList();
                LastConversationExpansionOverrides = overrides;
                if (changedOverrides.FirstOrDefault() is { } toggledId)
                    // Extend the data query to cover the toggled conversation's entries. It must extend,
                    // not replace: a conversation's start sits above everything it contains, so collapsing
                    // the one you're reading at the chat's tail would otherwise re-centre the window
                    // ~HalfLoadLimit rows past that start and unload the tail - dropping the very-last
                    // item, and with it the End sticky edge the view was pinned to.
                    dataQuery = dataQuery with {
                        ExistingLidRange = dataQuery.ExistingLidRange
                            .MinMaxWith(IdTileStack.LastLayer.GetTile(toggledId.StartEntryLid).Range),
                        StartOffset = -HalfLoadLimit,
                        EndOffset = HalfLoadLimit,
                    };
            }

            expandedConversations = defaultExpanded.SymmetricExcept(overrides);
            // A not-joined viewer can expand the live block to read the whole conversation before joining;
            // when expanded, stop hiding its tail so every entry [V, end] renders (the fold/skip logic
            // already drops the fold range for an expanded block). Collapsed, the hidden tail stands.
            if (liveConversation is { } liveConvExp && !amInLiveConversation && overlay == null
                && expandedConversations.Contains(liveConvExp.Id))
                hiddenLiveTailRange = default;
        }

        if (chatRangeMetaList.Count == 0)
            return ChatItems.Empty;

        var metaAt = CpuTimestamp.Now;
        List<Range<long>> idTiles;
        bool hasMoreBefore, hasMoreAfter;
        while (!TryGetIdTilesToLoad(dataQuery, chatRangeMetaList, out idTiles, out hasMoreBefore, out hasMoreAfter)) {
            var prevIdTileStart = chatRangeMetaList[0].PreviousLidTileStart;
            var nextIdTileStart = chatRangeMetaList[^1].NextLidTileStart;
            var prevChatRangeMetaTask = prevIdTileStart.HasValue
                ? Chats.GetChatRangeMeta(Session, chatId, prevIdTileStart.Value, cancellationToken)
                : Task.FromResult<ChatRangeMeta?>(null)!;
            var nextChatRangeMetaTask = nextIdTileStart.HasValue
                ? Chats.GetChatRangeMeta(Session, chatId, nextIdTileStart.Value, cancellationToken)
                : Task.FromResult<ChatRangeMeta?>(null)!;
            await Task.WhenAll(prevChatRangeMetaTask, nextChatRangeMetaTask).ConfigureAwait(false);
            var prevChatRangeMeta = await prevChatRangeMetaTask.ConfigureAwait(false);
            var nextChatRangeMeta = await nextChatRangeMetaTask.ConfigureAwait(false);
            if (prevChatRangeMeta == null! && nextChatRangeMeta == null!)
                break;

            if (prevChatRangeMeta != null)
                chatRangeMetaList.Insert(0, prevChatRangeMeta);
            if (nextChatRangeMeta != null!)
                chatRangeMetaList.Add(nextChatRangeMeta);
        }

        // A stand-in list is not cached: caching it would compound the synthesis on the next rebuild,
        // and the fresh result this build is shadowing lands in the follow-up rebuild anyway.
        if (lastKnownRangeMetaList == null && !isPrefetch)
            _lastChatRangeMetas[chatId] = chatRangeMetaList;

        if (!isPrefetch)
            _lastLoadZones[chatId] = (idTiles, showConversations);
        await PrefetchLoadZone(chatId, idTiles, showConversations, cancellationToken).ConfigureAwait(false);
        var loadAt = CpuTimestamp.Now;

        var conversationView = new ConversationViewState(
            showConversations, expandedConversations, hiddenLiveTailRange,
            liveBlockId, liveBlockFoldRange, materializedBlockId);
        var chatSendingMessages = Hub.SendingMessages.GetSendingMessages(chatId);
        var chatSendingMessagesWrapper = new IgnoreComputeArg<ChatSendingMessagesAccessor>(chatSendingMessages);
        var tiles = new List<VirtualListTile<ChatMessage>>();
        var hasVeryFirstItem = idTiles.Count > 0 && idTiles[0].Start <= chatLidRange.Start;
        var prevMessage = hasVeryFirstItem ? ChatMessage.Welcome(chatId) : null;
        var alreadyAddedConversationHeaders = new HashSet<ConversationId>();
        var alreadyAddedConversationCards = new HashSet<ConversationId>();
        var alreadyAddedLiveHeaders = new HashSet<ConversationId>();
        // The single tail tile merging optimistic "sending"/new/audio-recording messages - picked once so a
        // live session's beyond-end conversation tiles can't become a second tail (duplicate @key crash).
        var tailTileIndex = idTiles.FindIndex(t => t.Contains(chatLidRange.End - 1));
        if (tailTileIndex < 0 && !hasMoreAfter)
            tailTileIndex = idTiles.Count - 1;
        for (var tileIndex = 0; tileIndex < idTiles.Count; tileIndex++) {
            var idTile = idTiles[tileIndex];
            var lastReadEntryLid = shownReadyEntryLid;
            if (lastReadEntryLid < idTile.Start)
                lastReadEntryLid = 0;
            else if (shownReadyEntryLid >= idTile.End - 1)
                lastReadEntryLid = long.MaxValue;
            var isLastTile = tileIndex == tailTileIndex;
            var tile = await GetTile(
                    chatId,
                    chat.Rules.Author?.Id,
                    idTile,
                    conversationView.NarrowTo(idTile.MoveStart(-IdTileStack.FirstLayer.TileSize)),
                    prevMessage,
                    lastReadEntryLid,
                    isLastTile ? chatLidRange.End : null,
                    chatSendingMessagesWrapper,
                    cancellationToken)
                .ConfigureAwait(false);
            if (tile.Items.Count == 0)
                continue;

            if (expandedConversations.Count > alreadyAddedConversationHeaders.Count) {
                // Find conversation headers
                var filteredItems = tile.Items
                    .Where(chatMessage => chatMessage is not ConversationHeader conversationHeader
                        || alreadyAddedConversationHeaders.Add(conversationHeader.Conversation!.Id))
                    .ToList();
                if (filteredItems.Count != tile.Items.Count)
                    tile = tile with { Items = filteredItems };
            }
            else if (alreadyAddedConversationHeaders.Count > 0)
                // Skip the first conversation header if already added
                if (tile.Items[0] is ConversationHeader conversationHeader
                    && alreadyAddedConversationHeaders.Contains(conversationHeader.Conversation!.Id))
                    tile = tile with { Items = tile.Items.Skip(1).ToList() };
            if (tile.Items.Count == 0)
                continue;

            // Every tile a conversation spans re-emits its card, and the duplicate @key kills the circuit.
            var dedupedItems = tile.Items
                .Where(chatMessage => chatMessage is not ConversationMessage conversationMessage
                    || alreadyAddedConversationCards.Add(conversationMessage.Conversation!.Id))
                .ToList();
            if (dedupedItems.Count != tile.Items.Count)
                tile = tile with { Items = dedupedItems };
            if (tile.Items.Count == 0)
                continue;

            // Same problem for the live block's sticky header, emitted alongside the card.
            var dedupedHeaders = tile.Items
                .Where(chatMessage => chatMessage is not LiveConversationHeader liveConversationHeader
                    || alreadyAddedLiveHeaders.Add(liveConversationHeader.Conversation!.Id))
                .ToList();
            if (dedupedHeaders.Count != tile.Items.Count)
                tile = tile with { Items = dedupedHeaders };
            if (tile.Items.Count == 0)
                continue;

            if (tile.Items[0].Equals(prevMessage)) {
                // Skip the first item if it's the same as the previous tile - e.g., a large conversation that spans across multiple t
                tile = tile with { Items = tile.Items.Skip(1).ToList() };
                if (tile.Items.Count == 0)
                    continue;
            }
            tiles.Add(tile);
            prevMessage = tile.Items[^1];
        }

        // A chat whose only entries are removed loads zero id-tiles, so the per-tile loop above never
        // runs and never merges the optimistic "sending" messages. Build the tail tile explicitly for
        // that case so they still surface - and so this query depends on OnNewMessagesChanged. When tiles
        // *are* loaded, the loop's last tile already carries this merge (see isLastTile above), so we must
        // not append a second tail tile here - doing so duplicated the trailing conversation header's @key.
        if (idTiles.Count == 0 && !hasMoreAfter) {
            var tailRange = IdTileStack.FirstLayer.GetTile(chatLidRange.End).Range;
            var tailTile = await GetTile(
                    chatId,
                    chat.Rules.Author?.Id,
                    tailRange,
                    conversationView.NarrowTo(tailRange.MoveStart(-IdTileStack.FirstLayer.TileSize)),
                    prevMessage,
                    shownReadyEntryLid,
                    chatLidRange.End,
                    chatSendingMessagesWrapper,
                    cancellationToken)
                .ConfigureAwait(false);
            if (tailTile.Items.Count > 0) {
                tiles.Add(tailTile);
                hasVeryFirstItem = true;
            }
        }

        var items = tiles.SelectMany(t => t.Items).ToList();

        // Ensure welcome block is present when at the very beginning
        if (hasVeryFirstItem && (items.Count == 0 || items[0].Kind != ChatMessageKind.WelcomeBlock)) {
            var welcomeBlock = ChatMessage.Welcome(chatId);
            if (items.Count > 0)
                welcomeBlock.NextMessage = items[0];
            items.Insert(0, welcomeBlock);
        }

        // Remove NewMessagesLine if there are own messages after it -
        // own messages are never "new" to the sender
        var newMessagesLineIndex = items.FindIndex(i => i.Kind == ChatMessageKind.NewMessagesLine);
        if (newMessagesLineIndex >= 0)
            for (var i = newMessagesLineIndex + 1; i < items.Count; i++)
                if (items[i].Flags.HasFlag(ChatMessageFlags.IsOwnMessage)) {
                    items.RemoveAt(newMessagesLineIndex);
                    break;
                }

        // Re-link NextMessage along the final visual order on per-assembly copies.
        // Tile items come from the cached GetTile compute method and are shared across list
        // snapshots; mutating their NextMessage in place races between snapshots and corrupts
        // selection edges (selection-start/end in ChatEntryMessageView). Iterate in reverse so each
        // clone points at the next clone, keeping the forward chain private and consistent.
        for (var i = items.Count - 1; i >= 0; i--)
            items[i] = items[i].WithNextMessage(i + 1 < items.Count ? items[i + 1] : null);

        var direction = dataQuery.StartOffset != 0
            ? dataQuery.StartOffset < 0 ? -1 : 1
            : dataQuery.EndOffset != 0
                ? dataQuery.StartOffset < 0 ? -1 : 1
                : 0;
        if (direction != 0 && items.Count > 0 && !isPrefetch) {
            // prefetch next / previous tiles for next requests without awaiting
            var item = direction < 0 ? items[0] : items[^1];
            var prefetchDataQuery = new ChatDataQuery(
                IdTileStack.FirstLayer.GetTile(item.Id).Range,
                direction < 0 ? -LoadLimit : 0,
                direction < 0 ? 0 : LoadLimit);

            // use StopToken to cancel the prefetch task because we are not awaiting it
            _ = Task.Run(() => GetChatItemsInternal(chatId, prefetchDataQuery, shownReadyEntryLid, true, Hub.StopToken), Hub.StopToken);
        }

        var groupedItems = GroupAuthorMessages(items);

        if (!isPrefetch) {
            var totalMs = (long)startedAt.Elapsed.TotalMilliseconds;
            var liveMs = (long)(liveAt - startedAt).TotalMilliseconds;
            var metaMs = (long)(metaAt - liveAt).TotalMilliseconds;
            var loadMs = (long)(loadAt - metaAt).TotalMilliseconds;
            var buildMs = totalMs - liveMs - metaMs - loadMs;
            // A build that spanned a background stretch measures wall-clock time nobody waited for, so it
            // is reported nowhere - otherwise every percentile grows a tail of expected background waits
            // that read as stalls.
            var wasBackgrounded = cIsVisible is null
                || !cIsVisible.IsConsistent()
                || !cIsVisible.Value
                || !BrowserInfo.IsVisible.Value;
            var chatMs = (long)(chatAt - startedAt).TotalMilliseconds;
            var conversationMs = (long)(conversationAt - chatAt).TotalMilliseconds;
            var amInLiveMs = (long)(amInLiveAt - conversationAt).TotalMilliseconds;
            var snapshotMs = (long)(snapshotAt - amInLiveAt).TotalMilliseconds;
            var blockStateMs = (long)(liveAt - snapshotAt).TotalMilliseconds;
            if (!wasBackgrounded) {
                RecordPhase(totalMs, "total");
                RecordPhase(liveMs, "live");
                RecordPhase(chatMs, "live.chat");
                RecordPhase(conversationMs, "live.conversation");
                RecordPhase(amInLiveMs, "live.am-in-live");
                RecordPhase(snapshotMs, "live.snapshot");
                RecordPhase(blockStateMs, "live.block-state");
                RecordPhase(metaMs, "meta");
                RecordPhase(loadMs, "load");
                RecordPhase(buildMs, "build");
            }
            // The log carries the split because nothing else does on a device: the Meter has no exporter
            // there, and Sentry drops Activity tags (it stores no AC.* span attribute at all).
            if (totalMs > SlowBuildMs && !wasBackgrounded)
                Log.LogWarning(
                    "GetChatItems: {ChatId} took {TotalMs}ms (live {LiveMs} = chat {ChatMs} "
                    + "+ conversation {ConversationMs} + amInLive {AmInLiveMs} + snapshot {SnapshotMs} "
                    + "+ blockState {BlockStateMs}, meta {MetaMs}, load {LoadMs}, build {BuildMs}) "
                    + "for {DataQuery}",
                    chatId, totalMs, liveMs, chatMs, conversationMs, amInLiveMs,
                    snapshotMs, blockStateMs, metaMs, loadMs, buildMs, dataQuery.Format());
            else
                DebugLog?.LogDebug(
                    "GetChatItems: {ChatId} took {TotalMs}ms "
                    + "(live {LiveMs}, meta {MetaMs}, load {LoadMs}, build {BuildMs}, backgrounded {WasBackgrounded}) "
                    + "for {DataQuery}",
                    chatId, totalMs, liveMs, metaMs, loadMs,
                    buildMs, wasBackgrounded, dataQuery.Format());
        }

        if (expandedConversations.Count == 0 && liveBlockId == null)
            return new ChatItems(groupedItems, hasMoreBefore, hasMoreAfter);

        var liveBlockRange = liveBlockId is { } lbId
            ? new Range<long>(lbId.StartEntryLid, overlay?.BlockEndLid ?? long.MaxValue)
            : default;
        var groupedTiles = GroupExpandedConversations(
            groupedItems, liveBlockId, liveBlockRange, materializedBlockId, Log);
        return new ChatItems(groupedTiles, hasMoreBefore, hasMoreAfter);

        bool TryGetIdTilesToLoad(
            ChatDataQuery dataQuery1,
            IList<ChatRangeMeta> chatRangeMeta1,
            out List<Range<long>> idTiles1,
            out bool hasMoreBefore1,
            out bool hasMoreAfter1)
        {
            if (chatRangeMeta1.Count == 0) {
                idTiles1 = [];
                hasMoreBefore1 = false;
                hasMoreAfter1 = false;
                return true;
            }

            var hasPreviousIdTile = chatRangeMeta1[0].PreviousLidTileStart.HasValue;
            var hasNextIdTile = chatRangeMeta1[^1].NextLidTileStart.HasValue;
            var entryIdRanges = chatRangeMeta1
                .SelectMany(m => m.EntryLidRanges)
                .EnsureMonotonic();
            var conversationIdRanges = chatRangeMeta1
                .SelectMany(m => m.ConversationLidRanges)
                .EnsureMonotonic();

            // A collapsed conversation excludes its whole range (a single id-tile then renders its card).
            // The live block is the exception: its governed fold range can be narrower than its raw
            // range (lag/viewport hold entries back), so only the governed range is excluded here too -
            // otherwise the id tiles past the fold would never load, and the still-visible tail would
            // have nothing to render. Once closed, the persisted conversation's range starts at
            // ContextStartLid (its materialized id), not V (the live-era render id) - both identities
            // must resolve to the same governed range, or a session with pre-latch context would lose
            // its frozen tail's id-tiles after close.
            var excludedRanges = conversationIdRanges
                .Where(r => !expandedConversations.Contains(ConversationId.New(chatId, r.Start)))
                .Select(r => {
                    var rangeId = ConversationId.New(chatId, r.Start);
                    return rangeId == liveBlockId || rangeId == materializedBlockId ? liveBlockFoldRange : r;
                })
                .Where(r => !r.IsEmpty)
                .ToList();
            // hiddenLiveTailRange runs to long.MaxValue so the per-entry filter in GetChatItemsInternal
            // can hide spoken entries wherever they land. Excluding that whole span here would be a
            // different thing entirely: an id tile dropped here is never fetched, and the messages typed
            // during the call live in those tiles - so they vanished as soon as one crossed a tile
            // boundary. Bound it to what the call itself covers; past that only typed entries exist and
            // they must load. The bounded range still spans the block's start lid, which is what makes an
            // entry-less block (video-only, no summary yet) emit its card at all - liveBlockFoldRange is
            // empty then, so the select above contributes nothing for it.
            var hiddenTailToExclude = liveConversation is { } hiddenTailConversation
                ? new Range<long>(
                    hiddenLiveTailRange.Start,
                    Math.Max(hiddenLiveTailRange.Start, hiddenTailConversation.EndEntryLid + 1))
                : hiddenLiveTailRange;
            if (!hiddenLiveTailRange.IsEmpty && !hiddenTailToExclude.IsEmpty)
                excludedRanges.Add(hiddenTailToExclude);

            var merged = showConversations
                ? entryIdRanges
                    .Merge(excludedRanges, (ce, co) => ce.IntersectWith(co).IsEmpty ? (int)(ce.Start - co.Start) : 0)
                    .ToList()
                : entryIdRanges
                    .Select(idRange => (idRange, new Range<long>(0, 0)));

            var resultIdRanges = new List<Range<long>>();

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
                        AddRange(resultIdRanges, l);
                        AddRange(resultIdRanges, conversationStartRange);
                        pendingRight = r;
                    }
                    else {
                        AddRange(resultIdRanges, pendingRight ?? default);
                        pendingRight = null;
                        currentEntryRange = entryRange;
                    }
                }

                if (hasEntryRange && hasConversationRange) {
                    if (conversationRange.Contains(entryRange))
                        AddRange(resultIdRanges, conversationStartRange);
                    else {
                        var (l, r) = entryRange.Subtract(conversationRange);
                        AddRange(resultIdRanges, l);
                        AddRange(resultIdRanges, conversationStartRange);
                        pendingRight = r;
                    }
                }
                else if (hasEntryRange)
                    AddRange(resultIdRanges, entryRange);
                else if (hasConversationRange) {
                    // A card-only excluded range (e.g. the live block's hidden tail, which has no paired
                    // entry range) that arrives after an entry range left a pendingRight must flush that
                    // remainder FIRST. Otherwise its card is appended ahead of pendingRight, and AddRange's
                    // monotonic guard then silently drops the out-of-order pendingRight - its entries vanish,
                    // leaving a gap in the loaded tiles (a stale conversation card glued to the live block).
                    AddRange(resultIdRanges, pendingRight ?? default);
                    pendingRight = null;
                    AddRange(resultIdRanges, conversationStartRange);
                }
            }
            AddRange(resultIdRanges, pendingRight ?? default);

            var resultIdRangesSpan = resultIdRanges.AsSpan();
            var startIdWithOffset = GetIdWithOffset(
                resultIdRangesSpan,
                dataQuery1.ExistingLidRange.Start,
                dataQuery1.StartOffset);

            var endIdWithOffset = GetIdWithOffset(
                resultIdRangesSpan,
                dataQuery1.ExistingLidRange.End,
                dataQuery1.EndOffset);

            var hasFulfilledStart = (startIdWithOffset != null && HasOffsetReached(dataQuery1.StartOffset, startIdWithOffset.Value.ActualOffset)) || !hasPreviousIdTile;
            var hasFulfilledEnd = (endIdWithOffset != null && HasOffsetReached(dataQuery1.EndOffset, endIdWithOffset.Value.ActualOffset)) || !hasNextIdTile;
            var startEntryLid = startIdWithOffset?.Id ?? 0L;
            var endEntryLid = endIdWithOffset?.Id ?? long.MaxValue;
            // Keep the loaded range covering the visible range even when the scroll-driven offsets would
            // contract past it (they're derived from coordinate gaps ÷ average item size, which misfires
            // next to a very large item) — dropping a visible item would drop the scroll anchor and jump.
            var visibleLidRange = dataQuery1.VisibleLidRange;
            if (!visibleLidRange.IsEmpty) {
                startEntryLid = Math.Min(startEntryLid, visibleLidRange.Start);
                endEntryLid = Math.Max(endEntryLid, visibleLidRange.End);
            }
            idTiles1 = resultIdRanges
                .SkipWhile(r => r.End <= startEntryLid)
                .TakeWhile(r => r.Start <= endEntryLid)
                .SelectMany(r => IdTileStack.FirstLayer.GetCoveringTiles(r).Select(t => t.Range))
                .SkipWhile(r => r.End <= startEntryLid)
                .TakeWhile(r => r.Start <= endEntryLid)
                .EnsureMonotonic()
                .ToList();

            hasMoreBefore1 = hasPreviousIdTile || (hasFulfilledStart && idTiles1.Count > 0 && idTiles1[0].Start > resultIdRanges[0].Start);
            hasMoreAfter1 = hasNextIdTile || (hasFulfilledEnd && idTiles1.Count > 0 && idTiles1[^1].End < resultIdRanges[^1].End);
            return hasFulfilledStart && hasFulfilledEnd;

            static void AddRange(List<Range<long>> list, Range<long> range)
            {
                if (range.IsEmpty)
                    return;

                if (list.Count == 0 || list[^1].End <= range.Start)
                    list.Add(range);
            }

            static bool HasOffsetReached(long offset, long actualOffset)
            {
                if (offset < 0)
                    return actualOffset <= offset;
                return actualOffset >= offset;
            }
        }
    }

    // NOTE: Please don't add excessive computed dependencies without real reason - it might rerender whole chat view content
    [ComputeMethod(MinCacheDuration = 30, InvalidationDelay = 0.1)]
    protected virtual async Task<VirtualListTile<ChatMessage>> GetTile(
        ChatId chatId,
        AuthorId? currentAuthorId,
        Range<long> lidRange,
        ConversationViewState conversationView,
        ChatMessage? prevMessage,
        long lastReadEntryId,
        long? rangeEnd, /* specified only for last tile */
        IgnoreComputeArg<ChatSendingMessagesAccessor> chatSendingMessagesWrapper,
        CancellationToken cancellationToken = default)
    {
        // DebugLog?.LogDebug("GetTile: {ChatId} {IdRange} {LastReadEntryId}", chatId, idRange, lastReadEntryId);
        if (lidRange.IsEmptyOrNegative)
            throw new ArgumentOutOfRangeException(nameof(lidRange));

        var (showConversations, expandedConversations, hiddenLiveTailRange,
            liveBlockId, liveFoldRange, materializedBlockId) = conversationView;

        var chatSendingMessages = chatSendingMessagesWrapper.Value;
        var requestedIdRange = prevMessage == null
            ? lidRange.MoveStart(-IdTileStack.FirstLayer
                .TileSize) // to request previous item of requested range to properly render block star - we will drop it off
            : lidRange;
        var idRangesToSkip = Array.Empty<Range<long>>();
        var conversations = Array.Empty<Conversation>();
        var alreadyAddedConversationHeaders = new HashSet<ConversationId>();
        if (showConversations) {
            var conversationIdTile = ServerIdTileStack.LastLayer.GetTile(lidRange.Start); // Get largest tile that contains the requested range
            var conversationTile = await Conversations
                .GetTile(Session, chatId, conversationIdTile.Range, cancellationToken)
                .ConfigureAwait(false);
            conversations = conversationTile
                .Where(c => !c.EntryLidRange.IntersectWith(requestedIdRange).IsEmpty)
                .ToArray();
            if (materializedBlockId is { } matBlockId && liveBlockId is { } renderBlockId)
                // The closed live block keeps rendering under its live-era id, so the VirtualList @key
                // (derived from that id) and every rendered row survive the close unchanged.
                conversations = conversations
                    .Select(c => c.Id == matBlockId ? c with { Id = renderBlockId } : c)
                    .ToArray();
            idRangesToSkip = conversations
                .Where(c => !expandedConversations.Contains(c.Id))
                // The joined live block folds only its summarized range (empty pre-summary), never the whole
                // EntryLidRange - so entry V and the un-summarized tail stay visible.
                .Select(c => c.Id == liveBlockId ? liveFoldRange : c.EntryLidRange)
                .Where(r => !r.IsEmpty)
                .ToArray();
            // The live conversation's own persisted EntryLidRange is server-tracked (EndEntryLid-based),
            // so it can be narrower than the governed fold range once the boundary has advanced past
            // un-summarised rows (§4) - this tile then carries no matching Conversation to substitute
            // above, and the fold would never apply. Add it explicitly so folding is never limited by
            // how far the persisted conversation record itself currently reaches.
            if (liveBlockId is { } liveBlockConversationId && !expandedConversations.Contains(liveBlockConversationId)
                && !liveFoldRange.IsEmpty && conversations.All(c => c.Id != liveBlockConversationId))
                idRangesToSkip = [..idRangesToSkip, liveFoldRange];
        }
        // hiddenLiveTailRange deliberately stays out of idRangesToSkip: it must not drop whole tiles,
        // because what it hides is interleaved with typed messages that have to stay visible. It's
        // applied per entry below instead.
        var entryIdTiles = IdTileStack.FirstLayer
            .GetCoveringTiles(requestedIdRange)
            .Where(t => !idRangesToSkip.Any(range => range.Contains(t.Range)))
            .ToList();
        var tiles = await entryIdTiles
            .Select(t => Chats.GetTile(Session,
                chatId,
                t.Range,
                cancellationToken))
            .Collect(ApiConstants.Concurrency.High, cancellationToken)
            .ConfigureAwait(false);
        var entries = new List<ChatEntry>();
        foreach (var tile in tiles.OrderBy(t => t.LidTileRange.Start)) {
            foreach (var e in tile.Entries) {
                if (idRangesToSkip.Any(range => range.Contains(e.Id.LocalId)))
                    continue;
                // A non-joined viewer never sees the call's live transcript, and the range covering it
                // runs to long.MaxValue - so hiding by lid alone also swallowed everything typed during
                // the call, including the viewer's own just-posted message. Only what was spoken hides.
                if (e.HasAudio && !hiddenLiveTailRange.IsEmpty && hiddenLiveTailRange.Contains(e.Id.LocalId))
                    continue;

                var e2 = await chatSendingMessages.GetSelfOrSending(e).ConfigureAwait(false);
                entries.Add(e2);
            }
        }

        if (rangeEnd.HasValue) {
            // processing last tile
            chatSendingMessages.ProcessLoadedEntriesRange(rangeEnd.Value);
            var newMessages = await chatSendingMessages.GetNewMessages(currentAuthorId!, rangeEnd.Value).ConfigureAwait(false);
            entries.AddRange(newMessages);

            var audioRecordingEntry = await GetAudioRecordingEntry(chatId, currentAuthorId!, cancellationToken).ConfigureAwait(false);
            if (audioRecordingEntry is not null) {
                // Hide "Transcribing..." when a real transcription entry already exists
                // (i.e., the last entry from this author is streaming content).
                var (isSuppressed, expiresAt) = GetStreamingSuppression(entries, currentAuthorId!);
                if (!isSuppressed)
                    entries.Add(audioRecordingEntry);
                else if (expiresAt is { } moment) {
                    // The grace period goes stale on its own, so the tile has to come back when it
                    // does - at that moment, not a whole gap after this render, or the badge returns
                    // late by however much of the gap had already elapsed before it.
                    var delay = moment - Clocks.ServerClock.Now;
                    if (delay > TimeSpan.Zero)
                        Computed.GetCurrent()?.Invalidate(delay);
                }
            }
        }
        if (entries.Count == 0 && conversations.Length == 0)
            return new VirtualListTile<ChatMessage>(lidRange);

        var prevEntry = (ChatEntry?)null;
        var prevDate = prevMessage?.Date ?? DateOnly.MinValue;
        var isPrevUnread = false;
        var isPrevAudio = false;
        var hasVeryFirstItem = false;
        if (prevMessage is ChatEntryMessage prevEntryMessage) {
            prevEntry = prevEntryMessage.Entry;
            isPrevUnread = prevMessage.Flags.HasFlag(ChatMessageFlags.Unread);
            isPrevAudio = IsAudioKind(prevEntry);
            hasVeryFirstItem = prevMessage.Kind == ChatMessageKind.WelcomeBlock;
        }

        var messages = new List<ChatMessage>(entries.Count);
        // The joined live card is emitted even when expanded (it is the block header), so keep it on the
        // merge's right side regardless of expansion.
        var items = entries.Merge(
            conversations.Where(c => !expandedConversations.Contains(c.Id) || c.Id == liveBlockId),
            e => e.LocalId,
            c => c.Id.StartEntryLid);
        var isWelcomeBlockAdded = false;
        foreach (var (entry, conversation) in items) {
            var date = DateOnly.FromDateTime(DateTimeConverter.ToLocalTime(entry?.BeginsAt ?? conversation!.StartsAt));
            var shouldEmitCard = conversation != null
                && (!expandedConversations.Contains(conversation.Id) || conversation.Id == liveBlockId);
            // Paired tuple: a loaded entry shares the card's key lid (V) - emit the card before the entry.
            if (shouldEmitCard && entry != null)
                EmitConversationCard(conversation!, date);
            // The first tile widens its request by one tile to learn the preceding entry, so every
            // emission below is gated on the entry actually falling into this tile's own range.
            var shouldAddToResult = entry != null && (lidRange.Contains(entry.LocalId) || entry.IsSending);
            if (entry is { IsThreadStart: true }) {
                if (shouldAddToResult) {
                    var threadChatId = entry.ChatId.CreateThreadId(entry.LocalId);
                    var threadChat = await Chats.Get(Session, threadChatId, cancellationToken).ConfigureAwait(false);
                    if (threadChat is not null) {
                        var message = new ThreadMessage(entry, threadChat) {
                            Date = date,
                            PreviousMessage = prevMessage,
                            Kind = ChatMessageKind.Thread,
                            // Without this the thread is held by lid alone, and a block whose lid range
                            // has drifted from its conversation's then drops it - splitting the block.
                            Conversation = conversations.FirstOrDefault(c => c.EntryLidRange.Contains(entry.LocalId)),
                        };
                        messages.Add(message);
                        prevMessage = message;
                    }
                }
            }
            else if (entry != null) {
                // Ignore matched conversation
                var isClientMsg = entry.Version == 0;
                var expandedConversation = conversations.FirstOrDefault(c => c.EntryLidRange.Contains(entry.LocalId));
                var isBlockStart = IsBlockStart(prevEntry, entry);
                // Force a block start on the first entry crossing into the joined live block, so a same-author
                // pre-latch group can't swallow the block's tail entries.
                if (liveBlockId is { } jlId && entry.LocalId >= jlId.StartEntryLid
                    && (prevEntry == null || prevEntry.LocalId < jlId.StartEntryLid))
                    isBlockStart = true;
                // A same-author message that switches kind (transcribed vs not) starts a new block,
                // so its author header signals the kind change.
                if (!isBlockStart && prevEntry != null && prevEntry.AuthorId == entry.AuthorId
                    && isPrevAudio != IsAudioKind(entry))
                    isBlockStart = true;
                var isForward = entry.Forwarded is not null;
                var isPrevForward = prevEntry is not null && prevEntry.Forwarded is not null;
                var isForwardFromOtherChat = prevEntry?.Forwarded?.AuthorId.ChatId != entry.Forwarded?.AuthorId.ChatId;
                var isForwardFromOtherAuthor = prevEntry?.Forwarded?.AuthorId != entry.Forwarded?.AuthorId;
                var isForwardBlockStart = (isBlockStart && isForward)
                    || (isForward && (!isPrevForward || isForwardFromOtherChat));
                var isForwardAuthorBlockStart = isForwardBlockStart || (isForward && isForwardFromOtherAuthor);
                var isEntryUnread = entry.LocalId > lastReadEntryId && !isClientMsg;
                var isAudio = IsAudioKind(entry);
                var flags = default(ChatMessageFlags);
                if (isBlockStart)
                    flags |= ChatMessageFlags.BlockStart;
                if (isAudio)
                    flags |= ChatMessageFlags.Audio;
                if (isForwardBlockStart)
                    flags |= ChatMessageFlags.ForwardStart;
                if (isForwardAuthorBlockStart)
                    flags |= ChatMessageFlags.ForwardAuthorStart;
                if (isEntryUnread)
                    flags |= ChatMessageFlags.Unread;
                if (entry.AuthorId == currentAuthorId)
                    flags |= ChatMessageFlags.IsOwnMessage;
                if (shouldAddToResult) {
                    if (!isWelcomeBlockAdded && hasVeryFirstItem) {
                        var welcomeMessage = new ChatEntryMessage(entry) {
                            Kind = ChatMessageKind.WelcomeBlock,
                            ShouldSkipKey = true,
                            PreviousMessage = prevMessage,
                        };
                        if (prevMessage != null)
                            prevMessage.NextMessage = welcomeMessage;
                        messages.Add(welcomeMessage);
                        prevMessage = welcomeMessage;
                        isWelcomeBlockAdded = true;
                    }

                    if (isEntryUnread && !isPrevUnread && !flags.HasFlag(ChatMessageFlags.IsOwnMessage)) {
                        var newLineMessage = new ChatEntryMessage(entry) {
                            Kind = ChatMessageKind.NewMessagesLine,
                            ShouldSkipKey = true,
                            Date = date,
                            PreviousMessage = prevMessage,
                            Conversation = expandedConversation,
                        };
                        if (prevMessage != null)
                            prevMessage.NextMessage = newLineMessage;
                        messages.Add(newLineMessage);
                        prevMessage = newLineMessage;
                    }

                    // Conversation header goes before the date line (the joined live card is its own header).
                    if (expandedConversation != null && expandedConversation.Id != liveBlockId
                        && alreadyAddedConversationHeaders.Add(expandedConversation.Id)
                        && (prevMessage == null
                            || prevMessage.Kind == ChatMessageKind.WelcomeBlock
                            || prevMessage.Id < expandedConversation.Id.StartEntryLid)) {
                        // Add a conversation header only if it wasn't added before
                        var conversationHeaderMessage = new ConversationHeader(expandedConversation) {
                            Kind = ChatMessageKind.ConversationStart,
                            ShouldSkipKey = true,
                            Date = date,
                            PreviousMessage = prevMessage,
                        };
                        if (prevMessage != null)
                            prevMessage.NextMessage = conversationHeaderMessage;
                        messages.Add(conversationHeaderMessage);
                        prevMessage = conversationHeaderMessage;
                        // This entry opens the conversation - the header already spaces it off. A date line
                        // can land in between, so the entry can't tell from its own PreviousMessage.
                        flags |= ChatMessageFlags.FirstInConversation;
                    }
                    if (date != prevDate) {
                        var dateLineMessage = new ChatEntryMessage(entry) {
                            Kind = ChatMessageKind.DateLine,
                            ShouldSkipKey = true,
                            Date = date,
                            PreviousMessage = prevMessage,
                            Conversation = expandedConversation,
                        };
                        if (prevMessage != null)
                            prevMessage.NextMessage = dateLineMessage;
                        messages.Add(dateLineMessage);
                        prevMessage = dateLineMessage;
                    }
                    // This messages should have special visibility keys to
                    // 1) do not update read position
                    // 2) do not affect get tiles query
                    var messageKind = ChatMessageKind.None;
                    if (isClientMsg)
                        messageKind = entry.SendingTag switch {
                            SendingMessage => ChatMessageKind.SendingNewMessage,
                            AudioRecordingMessageTag => ChatMessageKind.AudioRecordingMessage,
                            _ => throw new ArgumentOutOfRangeException(nameof(ChatEntry.SendingTag)),
                        };
                    var message = new ChatEntryMessage(entry) {
                        Date = date,
                        Flags = flags,
                        ShouldSkipKey = isClientMsg,
                        Kind = messageKind,
                        PreviousMessage = prevMessage,
                        Conversation = expandedConversation,
                    };
                    if (prevMessage != null)
                        prevMessage.NextMessage = message;
                    messages.Add(message);
                    prevMessage = message;

                    if (expandedConversation != null && expandedConversation.Id != liveBlockId)
                        if (entry.Id.LocalId == expandedConversation.EndEntryLid) {
                            var conversationFooterMessage = new ConversationFooter(expandedConversation) {
                                Kind = ChatMessageKind.ConversationEnd,
                                ShouldSkipKey = true,
                                Date = date,
                                PreviousMessage = prevMessage,
                            };
                            prevMessage.NextMessage = conversationFooterMessage;
                            messages.Add(conversationFooterMessage);
                            prevMessage = conversationFooterMessage;
                        }
                }
                prevEntry = entry;
                isPrevUnread = isEntryUnread;
                isPrevAudio = isAudio;
            }
            else if (shouldEmitCard)
                EmitConversationCard(conversation!, date);
            prevDate = date;
        }
        if (messages.Count > 0 && !lidRange.Contains(messages[0].Id))
            // Remove messages that are outside requested range
            messages.RemoveAll(m =>
                (m is ChatEntryMessage && !lidRange.Contains(m.Id))
                || (m is ConversationMessage cm && lidRange.IntersectWith(cm.Conversation!.EntryLidRange).IsEmpty));
        return new VirtualListTile<ChatMessage>($"tile:{lidRange.Format()}", messages);

        void EmitConversationCard(Conversation conversation, DateOnly date)
        {
            // Only the ONGOING live block gets the split-out live (animated) header. Once the session
            // closes (materializedBlockId set), the block stays frozen but renders as a completed
            // conversation: the card falls back to its own regular header instead.
            var hasSplitHeader = conversation.Id == liveBlockId && materializedBlockId == null;
            if (hasSplitHeader) {
                var header = new LiveConversationHeader(conversation) {
                    Kind = ChatMessageKind.LiveConversationHeader,
                    Date = date,
                    PreviousMessage = prevMessage,
                };
                if (prevMessage != null)
                    prevMessage.NextMessage = header;
                messages.Add(header);
                prevMessage = header;
            }
            var message = new ConversationMessage(conversation) {
                Kind = ChatMessageKind.ConversationStart,
                Date = date,
                PreviousMessage = prevMessage,
                HasSplitHeader = hasSplitHeader,
                HasSplitFooter = conversation.Id == liveBlockId,
            };
            // Can't skip adding a conversation message even if it's the same as previous message
            // Note: the same conversation can be returned by different id tiles as it spans across multiple tiles
            if (prevMessage != null)
                prevMessage.NextMessage = message;
            messages.Add(message);
            prevMessage = message;
        }
    }

    [ComputeMethod]
    public virtual async Task<IReadOnlyList<ChatEntry>> GetThreadPreviewEntries(
        ChatId chatId,
        int count = 2,
        long minLid = 0,
        CancellationToken cancellationToken = default)
        => await Chats.ReadReverse(Session, chatId, cancellationToken)
            .TakeWhile(x => x.LocalId >= minLid) // reverse order: stop before the conversation start (minLid = V)
            .Where(x => !x.IsSystemEntry)
            .Take(count)
            .Reverse()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    [ComputeMethod]
    protected virtual async Task<ChatEntry?> GetAudioRecordingEntry(ChatId chatId, AuthorId ownAuthorId, CancellationToken cancellationToken)
    {
        var state = await Hub.AudioRecorder.State.Use(cancellationToken).ConfigureAwait(false);
        // ChatId is the microphone session: set by MarkStarting, cleared by MarkStopped. Deliberately NOT
        // RecordingStartTime, which OnRecordingStateChange sets and clears per *utterance* from the VAD —
        // gating on it made this entry blink in and out for the whole session (measured: present ~5s out
        // of every ~52s). While the mic is on for a chat there must always be an entry standing in for
        // the upcoming transcript.
        var isRecordingInTheChat = state.ChatId == chatId;
        if (!isRecordingInTheChat)
            return null;

        var chatEntry = _audioRecordingEntry;
        if (chatEntry?.ChatId == chatId)
            return chatEntry; // Cache chat entry to enable reusing MessageView.

        var entryId = ChatEntryId.New(chatId, long.MaxValue);
        chatEntry = new TextEntry(entryId, 0) {
            AuthorId = ownAuthorId,
            Content = "",
            // The entry now outlives the first utterance, so RecordingStartTime is still epoch when the
            // mic has just been switched on.
            BeginsAt = state.RecordingStartTime.EpochOffsetTicks > 0
                ? state.RecordingStartTime
                : Clocks.SystemClock.Now,
            // TODO: rename SendingTag to ClientTag.
            // We need not null value because otherwise entry will not be included in the result view.
            SendingTag = new AudioRecordingMessageTag(),
            ClientUid = Guid.NewGuid().ToString(),
        };
        // Owner.RegisterEntryByClientId(chatEntry);
        _audioRecordingEntry = chatEntry;
        return chatEntry;
    }

    // Private methods

    private static List<ChatMessage> GroupAuthorMessages(IEnumerable<ChatMessage> messages)
    {
        var result = new List<ChatMessage>();
        var groupedItems = new List<ChatEntryMessage>();

        foreach (var message in messages)
            if (message is not ChatEntryMessage cem || message.IsReplacement) {
                AddGroupToResult();
                groupedItems = [];
                result.Add(message);
            }
            else if (message.Flags.HasFlag(ChatMessageFlags.BlockStart)) {
                AddGroupToResult();
                groupedItems = [cem];
            }
            else
                groupedItems.Add(cem);

        AddGroupToResult();
        return result;

        void AddGroupToResult()
        {
            if (groupedItems.Count == 0)
                return;

            // Always wrap into a group, even for a single message: this keeps the virtual list
            // item key stable ("{firstId}-group") when the next same-author message joins the block,
            // so the first message isn't torn down and re-created on every follow-up message.
            result.Add(new ChatEntryAuthorGroup(groupedItems[0].Entry.AuthorId, groupedItems) {
                Conversation = groupedItems[0].Conversation,
            });
        }
    }

    // It's internal to be accessible from tests
    internal static List<ChatMessage> GroupExpandedConversations(
        IReadOnlyList<ChatMessage> messages, ConversationId? liveBlockId, Range<long> liveBlockRange,
        ConversationId? materializedBlockId, ILogger? log)
    {
        // Wrap every item of one expanded conversation into a single ExpandedConversationMessage in
        // source order - the block is the sticky containing element for the conversation header and
        // author avatars (position: sticky is bounded by the block <li>), and the VirtualList still
        // virtualizes its inner .item rows. Interrupting ThreadMessages (Conversation == null) whose
        // lid falls inside the conversation's range stay inside the block, in place - so the block's
        // first/last leaf are the true window ends (VirtualListData.KeyRange stays correct) and there
        // is exactly one block per conversation (unique StartEntryLid @key, no duplicate-@key crash).
        var result = new List<ChatMessage>();
        Conversation? blockConversation = null;
        var blockItems = new List<ChatMessage>();
        var emittedBlockIds = new HashSet<ConversationId>();

        // Index of each conversation's last item. A Conversation-less item before it is an interruption
        // *inside* that conversation, not its end - which is the one question the lid ranges below can't
        // answer reliably: a frozen BlockEndLid, or an entry not yet part of the conversation, falls
        // outside them, ends the block, and lets the items after it open a second one.
        var lastIndexById = new Dictionary<ConversationId, int>();
        for (var i = 0; i < messages.Count; i++)
            if (messages[i].Conversation is { } itemConversation)
                lastIndexById[itemConversation.Id] = i;

        for (var i = 0; i < messages.Count; i++) {
            var item = messages[i];
            var conversation = item.Conversation;
            var isLiveBlock = liveBlockId != null && blockConversation?.Id == liveBlockId;
            // A Conversation-less item is held by its lid alone, so the live block's range must cover the
            // conversation as well: the frozen BlockEndLid stops at the summary that was live when the
            // viewer left, and a later summary stretches the conversation past it.
            var blockRange = blockConversation == null
                ? default
                : isLiveBlock
                    ? new Range<long>(
                        Math.Min(liveBlockRange.Start, blockConversation.EntryLidRange.Start),
                        Math.Max(liveBlockRange.End, blockConversation.EntryLidRange.End))
                    : blockConversation.EntryLidRange;
            var belongs = blockConversation != null
                && (conversation != null
                    ? conversation.Id == blockConversation.Id
                    : i < lastIndexById.GetValueOrDefault(blockConversation.Id, -1)
                        // Range.Contains excludes End, so a still-live [V, ∞) range needs the open-ended
                        // form - a closed block carries a real BlockEndLid and uses Contains as usual.
                        // Keeps the frozen tail past the conversation's last item inside the block.
                        || (blockRange.End == long.MaxValue
                            ? item.Id >= blockRange.Start
                            : blockRange.Contains(item.Id)));
            if (belongs) {
                blockItems.Add(item);
                continue;
            }

            FinalizeBlock();
            // A joined live card (a ConversationMessage) starts its block; other ConversationMessages don't.
            var startsBlock = conversation != null
                && (item is not ConversationMessage || conversation.Id == liveBlockId);
            if (startsBlock) {
                blockConversation = conversation;
                blockItems.Add(item);
            }
            else
                result.Add(item);
        }
        FinalizeBlock();
        return result;

        void FinalizeBlock()
        {
            if (blockConversation == null)
                return;

            var conversation = blockConversation;
            var items = blockItems;
            blockConversation = null;
            blockItems = [];

            // Every block is keyed by conversation.Id.StartEntryLid, so a second one for the same
            // conversation is a duplicate @key - Blazor throws and tears the whole render down. The
            // loop above should make this unreachable; if it ever isn't, emit the items inline (they
            // keep their own keys and their order) and lose only the visual grouping.
            if (!emittedBlockIds.Add(conversation.Id)) {
                log?.LogWarning(
                    "GroupExpandedConversations: conversation {ConversationId} would open a second block",
                    conversation.Id);
                result.AddRange(items);
                return;
            }

            // The ongoing live block closes with the animated live footer; a completed (materialized)
            // block ends with the same regular conversation footer every expanded conversation gets.
            // It's appended as the block's last child rather than emitted inline at EndEntryLid (the
            // GetTile path), because the frozen tail kept inside the block extends past that lid.
            if (conversation.Id == liveBlockId)
                items.Add(materializedBlockId == null
                    ? new LiveConversationFooter(conversation) {
                        Kind = ChatMessageKind.ConversationEnd,
                        PreviousMessage = items.Count > 0 ? items[^1] : null,
                    }
                    : new ConversationFooter(conversation) {
                        Kind = ChatMessageKind.ConversationEnd,
                        PreviousMessage = items.Count > 0 ? items[^1] : null,
                    });
            result.Add(new ExpandedConversationMessage(conversation, items));
        }
    }

    private static void RecordPhase(long elapsedMs, string phase)
        => GetChatItemsDuration.Record(elapsedMs, new KeyValuePair<string, object?>("phase", phase));

    private Task PrefetchLoadZone(
        ChatId chatId,
        List<Range<long>> idTiles,
        bool showConversations,
        CancellationToken cancellationToken)
        => Task.Run(async () => {
            // GetTile widens its request by one tile for the first tile (prevMessage == null, so it
            // needs the preceding entry to render the block start). Prefetching only idTiles left
            // that one always uncached, costing a serial round-trip on every rebuild.
            var prefetchEntriesTask = idTiles
                .SelectMany((r, i) => IdTileStack.FirstLayer.GetCoveringTiles(
                    i == 0 ? r.MoveStart(-IdTileStack.FirstLayer.TileSize) : r))
                .Select(t => t.Range)
                .Where(r => r.Start >= 0)
                .EnsureMonotonic()
                .Select(r => Chats.GetTile(Session, chatId, r, cancellationToken))
                .Collect(ApiConstants.Concurrency.High, cancellationToken);
            var prefetchConversationsTask = showConversations
                ? idTiles
                    .Select(r => ServerIdTileStack.LastLayer.GetTile(r.Start).Range)
                    .EnsureMonotonic()
                    .Select(r => Conversations.GetTile(Session, chatId, r, cancellationToken))
                    .Collect(ApiConstants.Concurrency.High, cancellationToken)
                : Task.CompletedTask;
            var prefetchChatInfoTask = PrefetchChatInfo(chatId, cancellationToken);
            await Task.WhenAll(prefetchEntriesTask, prefetchConversationsTask, prefetchChatInfoTask).ConfigureAwait(false);
        }, cancellationToken);

    // Serves the last fresh range-meta list while a refetch is in flight, so a rebuild triggered by a
    // new entry renders before the round-trip instead of after it. The tail is synthesized from
    // chatLidRange: GetIdRange is the subscribed new-entry signal and the view just read it, so lids past
    // the cached tail's entry ranges are known to exist before any meta describes them. A lid removed
    // meanwhile is harmless - the synthesized ranges over-claim, and GetTile (the content source) simply
    // doesn't return it. UseIfReady invalidates the enclosing computed when the fresh result lands, so a
    // build made from a stand-in is always followed by one made from fresh meta.
    private List<ChatRangeMeta>? UseRangeMetaOrLastKnown(
        ChatId chatId,
        Task<ChatRangeMeta[]> freshMetaTask,
        Range<long> metaIdTilesRange,
        Range<long> chatLidRange,
        bool isPrefetch)
    {
        // The first read still awaits - standing in on nothing would render an empty chat. A failed fetch
        // also awaits: UseIfReady doesn't invalidate on failure, so standing in for one would pin the
        // stale value with no retry trigger, while awaiting propagates the error exactly as it did before.
        var computed = Computed.Current;
        if (isPrefetch || computed == null || metaIdTilesRange.IsEmpty
            || freshMetaTask.IsFaulted || freshMetaTask.IsCanceled
            || !_lastChatRangeMetas.TryGetValue(chatId, out var lastKnown)
            || lastKnown.Count == 0)
            return null;

        // UseIfReady before anything else: checking IsCompleted separately would race - a task completing
        // between the check and the hookup would serve a stand-in that nothing ever invalidates.
        if (freshMetaTask.UseIfReady(null!, computed) != null)
            return null;

        var first = lastKnown[0];
        var last = lastKnown[^1];
        if (first.LidRange.Start > metaIdTilesRange.Start && first.PreviousLidTileStart != null)
            return null; // The query reaches above the cached span; only the fresh fetch knows that part

        // Copied so the meta walk in GetChatItemsInternal can't mutate the cached list
        var result = new List<ChatRangeMeta>(lastKnown);
        var targetEnd = Math.Min(metaIdTilesRange.End, chatLidRange.End);
        if (last.NextLidTileStart == null) {
            // The cached list ended at the chat's tail, so every lid past its entry ranges is a new entry
            var entryRanges = last.EntryLidRanges;
            if (entryRanges.Length == 0)
                return null; // A tail with no entries can't anchor the extension

            if (targetEnd > entryRanges[^1].End)
                result[^1] = last with {
                    LidRange = new Range<long>(last.LidRange.Start, Math.Max(last.LidRange.End, targetEnd)),
                    EntryLidRanges = [..entryRanges[..^1], new Range<long>(entryRanges[^1].Start, targetEnd)],
                };
        }
        else if (last.LidRange.End < targetEnd)
            return null; // The query reaches below the cached span; only the fresh fetch knows that part

        return result;
    }

    private static T? UseOrLastKnown<T>(
        ConcurrentDictionary<ChatId, T> lastKnownValues,
        ChatId chatId,
        Task<T> task,
        bool isPrefetch)
        where T : class
    {
        // The first read still awaits (same contract as LiveSessionUI.UseOrLastKnown), and so does a
        // failed fetch - standing in for one would pin the stale value with no retry trigger.
        var computed = Computed.Current;
        if (isPrefetch || computed == null || task.IsFaulted || task.IsCanceled
            || !lastKnownValues.TryGetValue(chatId, out var lastKnown))
            return null;

        return task.UseIfReady(null!, computed) ?? lastKnown;
    }

    private Task PrefetchChatInfo(ChatId chatId, CancellationToken cancellationToken)
        // DebugLog?.LogDebug("PrefetchTiles: {ChatId} {IdRange}", chatId, idRange);
        => BackgroundTask.Run(async () => {
                // We are making following calls during chat view rendering:
                // IChats.Get:3
                // IChats.GetIdRange:4
                // IChats.GetRules:3
                // IAuthors.ListAuthorIds:3
                // IAuthors.GetPresence:4
                // IRoles.ListOwnerIds:3
                // IReactions.ListSummaries:3
                // IAuthors.Get:4
                var chatTask = Chats.Get(Session, chatId, cancellationToken);
                var idRangeTask = Chats.GetIdRange(Session, chatId, cancellationToken);
                var rulesTask = Chats.GetRules(Session, chatId, cancellationToken);
                var authorsTask = Authors.ListAuthorIds(Session, chatId, cancellationToken);
                var isEmptyTask = IsEmpty(chatId, cancellationToken);

                await Task.WhenAll(chatTask,
                        idRangeTask,
                        rulesTask,
                        authorsTask,
                        isEmptyTask)
                    .ConfigureAwait(false);
            },
            Log,
            "Error prefetching chat tiles.",
            CancellationToken.None);

    // What the author header signals as a kind: a streaming entry is audio that has not landed yet,
    // and the recording placeholder stands in for one while carrying neither Audio nor a
    // ContentStreamId - only its tag says so.
    private static bool IsAudioKind(ChatEntry entry)
        => entry.HasAudio
            || entry.IsContentStreaming
            || entry.SendingTag is AudioRecordingMessageTag;

    private static bool IsBlockStart(ChatEntry? prevEntry, ChatEntry entry)
    {
        if (prevEntry == null)
            return true;
        if (prevEntry.AuthorId != entry.AuthorId)
            return true;

        var prevEndsAt = prevEntry.EndsAt ?? prevEntry.BeginsAt;
        return entry.BeginsAt - prevEndsAt >= BlockStartTimeGap;
    }

    private static (long Id, int ActualOffset)? GetIdWithOffset(ReadOnlySpan<Range<long>> ranges, long anchorId, int requestedOffset)
    {
        if (ranges.IsEmpty)
            return null;

        if (requestedOffset == 0)
            return (anchorId, 0);

        var isForward = requestedOffset > 0;
        var index = ranges.BinarySearch(r => r.End > anchorId);
        if (index < 0) {
            if (isForward)
                return null;

            index = ranges.Length - 1;
        }

        var remaining = Math.Abs((long)requestedOffset);
        var travelled = 0L;

        var currentId = anchorId;
        while (remaining > 0) {
            var r = ranges[index];

            if (isForward) {
                // start position inside this range
                var begin = Math.Max(r.Start, currentId + 1);
                var capacity = r.End - begin;

                if (capacity <= 0) {
                    if (++index >= ranges.Length)
                        break;

                    continue;
                }

                if (remaining <= capacity) {
                    currentId = begin + remaining - 1;
                    travelled += remaining;
                    remaining = 0;
                }
                else {
                    currentId = r.End - 1; // last item of this range
                    travelled += capacity;
                    remaining -= capacity;
                    if (++index >= ranges.Length)
                        break;
                }
            }
            else {
                var end = Math.Min(r.End - 1, currentId - 1);
                var capacity = end - r.Start + 1;

                if (capacity <= 0) {
                    if (--index < 0)
                        break;

                    continue;
                }

                if (remaining <= capacity) {
                    currentId = end - remaining + 1;
                    travelled += remaining;
                    remaining = 0;
                }
                else {
                    currentId = r.Start;
                    travelled += capacity;
                    remaining -= capacity;
                    if (--index < 0)
                        break;
                }
            }
        }

        int actualOffset = (int)(isForward ? travelled : -travelled);
        return (currentId, actualOffset);
    }

    // Null ExpiresAt with IsSuppressed means only a change can lift it - there is nothing to time out.
    private (bool IsSuppressed, Moment? ExpiresAt) GetStreamingSuppression(
        IList<ChatEntry> entries,
        AuthorId ownAuthorId)
    {
        // Depth counts the caller's own audio entries rather than list positions, so anyone else
        // talking in between can't push the transcript out of the window.
        // Server clock: EndsAt below is server-stamped, and a device clock a few seconds off either
        // way turns the grace period into "always" or "never".
        var now = Clocks.ServerClock.Now;
        var scanned = 0;
        for (var i = entries.Count - 1; i >= 0 && scanned < OwnAudioEntryScanDepth; i--) {
            var entry = entries[i];
            if (entry.AuthorId != ownAuthorId || !(entry.HasAudio || entry.IsContentStreaming))
                continue;
            if (entry.IsContentStreaming)
                return (true, null);

            // IsContentStreaming goes false the moment recognition closes one entry and stays false
            // until it opens the next, while the client keeps rendering that entry as streaming from
            // its own reader. Without this grace period "Transcribing..." flashes into every gap,
            // right next to a transcript the user can see is still running.
            if (entry.EndsAt is { } endsAt && now - endsAt < StreamingEntryGap)
                return (true, endsAt + StreamingEntryGap);

            scanned++;
        }

        return (false, null);
    }

    // Nested types
    private record AudioRecordingMessageTag;
}
