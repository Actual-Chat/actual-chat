using ActualChat.Kvas;
using CommunityToolkit.HighPerformance;

namespace ActualChat.UI.Blazor.App.Services;

public record ChatItems(IReadOnlyList<ChatMessage> Items, bool HasBefore, bool HasAfter)
{
    public static readonly ChatItems Empty = new([], false, false);
}

public sealed record ConversationViewState(
    bool ShowConversations,
    IImmutableSet<ConversationId> ExpandedConversations,
    Range<long> HiddenLiveTailRange);

public partial class ChatUI
{
    private static readonly TimeSpan BlockStartTimeGap = TimeSpan.FromSeconds(120);

    public static readonly TileStack<long> IdTileStack = Constants.Chat.ViewIdTileStack;
    public static readonly TileStack<long> ServerIdTileStack = Constants.Chat.ServerIdTileStack;
    public static readonly int SecondTileSize = (int)IdTileStack.LastLayer.TileSize; // 20

    private volatile ChatEntry? _audioRecordingEntry;

    private IImmutableSet<ConversationId> LastExpandedConversations { get; set; } =
        ImmutableHashSet<ConversationId>.Empty;

    public int HalfLoadLimit => BrowserInfo.IsMobile ? SecondTileSize : SecondTileSize * 2; // 20 for mobile
    public int LoadLimit => BrowserInfo.IsMobile ? SecondTileSize * 2 : SecondTileSize * 4; // 40 for mobile

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
        var chat = await Chats.Get(Session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return ChatItems.Empty;

        var liveConversation = await Hub.LiveConversationUI.Get(chatId, cancellationToken).ConfigureAwait(false);
        var amInLiveConversation = liveConversation != null
            && await Hub.LiveConversationUI.AmIInLiveConversation(chatId, cancellationToken).ConfigureAwait(false);
        // Non-joined users see the live block's summary only — no live entries. The synthetic
        // conversation already collapses [Start, EndEntryLid]; hide the un-summarized tail too.
        var hiddenLiveTailRange = liveConversation is { } liveConv && !amInLiveConversation
            ? new Range<long>(liveConv.EndEntryLid + 1, long.MaxValue)
            : default;

        var metaIdTiles = ServerIdTileStack.LastLayer.GetCoveringTiles(dataQuery.ExistingLidRange.Expand(LoadLimit))
            .Where(t => t.Start >= 0)
            .ToList();
        var chatRangeMetaList = (await metaIdTiles
                .Select(metaIdTile
                    => Chats.GetChatRangeMeta(Session, chatId, metaIdTile.Range.Start, cancellationToken))
                .Collect(cancellationToken)
                .ConfigureAwait(false))
            .OrderBy(m => m.LidRange.Start)
            .ThenByDescending(m => m.LidRange.Size()) // ChatRangeMeta can be overlapping, so we need to keep the largest
            .EnsureMonotonic(Comparer<ChatRangeMeta>.Create((a, b) => a.LidRange.Start.CompareTo(b.LidRange.Start)))
            .ToList();

        var showConversations = (chat.IsSummarized ?? false) || liveConversation != null;
        if (showConversations && dataQuery.Navigation is { ShouldRestoreViewPosition: false, KeepConversationsCollapsed: false }) {
            var conversationRanges = chatRangeMetaList
                .SelectMany(rm => rm.ConversationLidRanges)
                .EnsureMonotonic()
                .ToList();
            var navigateToId = dataQuery.Navigation.EntryLid;
            var index = conversationRanges.AsSpan().BinarySearch(r => r.Contains(navigateToId) || r.Start > navigateToId);
            if (index >= 0) {
                var conversationRange = conversationRanges[index];
                if (conversationRange.Contains(navigateToId)) {
                    // Expand the conversation if its range contains the navigateToId
                    var conversationId = ConversationId.New(chatId, conversationRange.Start);
                    var currentExpandedConversations = ExpandedConversations.Value;
                    var newExpandedConversations =  currentExpandedConversations.Add(conversationId);
                    if (!ReferenceEquals(newExpandedConversations, currentExpandedConversations)) {
                        _expandedConversations.Value = newExpandedConversations;
                        // We don't need to navigate to the conversation start entry
                        LastExpandedConversations = newExpandedConversations;
                    }
                }
            }
        }

        IImmutableSet<ConversationId> expandedConversations = [];
        if (showConversations) {
            expandedConversations = await ExpandedConversations.Use(cancellationToken).ConfigureAwait(false);
            var changedExpand = expandedConversations.SymmetricExcept(LastExpandedConversations)
                .OrderBy(c => c.StartEntryLid)
                .ToList();
            LastExpandedConversations = expandedConversations;
            if (changedExpand.FirstOrDefault() is { } conversationId)
                // Adjust data query to load tiles around expanded conversation entries
                dataQuery = new ChatDataQuery(
                    IdTileStack.LastLayer.GetTile(conversationId.StartEntryLid).Range,
                    -HalfLoadLimit,
                    HalfLoadLimit);
        }

        Range<long> chatLidRange;
        using (Computed.BeginIsolation())
            chatLidRange = await Chats.GetIdRange(Session, chatId, cancellationToken).ConfigureAwait(false);

        if (chatRangeMetaList.Count == 0)
            return ChatItems.Empty;

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

        // Prefetch tiles for the loaded id ranges
        {
            await Task.Run(async () => {
                var prefetchEntriesTask = idTiles
                    .SelectMany(r => IdTileStack.FirstLayer.GetCoveringTiles(r))
                    .Select(t => t.Range)
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
            }, cancellationToken).ConfigureAwait(false);
        }

        var chatSendingMessages = Hub.SendingMessages.GetSendingMessages(chatId);
        var chatSendingMessagesWrapper = new IgnoreComputeArg<ChatSendingMessagesAccessor>(chatSendingMessages);
        var tiles = new List<VirtualListTile<ChatMessage>>();
        var hasVeryFirstItem = idTiles.Count > 0 && idTiles[0].Start <= chatLidRange.Start;
        var prevMessage = hasVeryFirstItem ? ChatMessage.Welcome(chatId) : null;
        var alreadyAddedConversationHeaders = new HashSet<ConversationId>();
        for (var tileIndex = 0; tileIndex < idTiles.Count; tileIndex++) {
            var idTile = idTiles[tileIndex];
            var lastReadEntryLid = shownReadyEntryLid;
            if (lastReadEntryLid < idTile.Start)
                lastReadEntryLid = 0;
            else if (shownReadyEntryLid >= idTile.End - 1)
                lastReadEntryLid = long.MaxValue;
            // When we're at the chat's newest end, the last loaded tile is the tail even if a trailing
            // run of removed entries leaves chatLidRange.End-1 outside it - and it's where the optimistic
            // "sending"/new messages must be merged.
            var isLastTile = idTile.Contains(chatLidRange.End - 1)
                || (!hasMoreAfter && tileIndex == idTiles.Count - 1);
            var tile = await GetTile(
                    chatId,
                    chat.Rules.Author?.Id,
                    idTile,
                    new ConversationViewState(showConversations, expandedConversations, hiddenLiveTailRange),
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
                    showConversations,
                    expandedConversations,
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

        if (expandedConversations.Count == 0)
            return new ChatItems(groupedItems, hasMoreBefore, hasMoreAfter);

        var groupedTiles = GroupExpandedConversations(groupedItems);
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

            var excludedRanges = conversationIdRanges
                .Where(r => !expandedConversations.Contains(ConversationId.New(chatId, r.Start)))
                .ToList();
            if (!hiddenLiveTailRange.IsEmpty)
                excludedRanges.Add(hiddenLiveTailRange);

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
                else if (hasConversationRange)
                    AddRange(resultIdRanges, conversationStartRange);
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

        var (showConversations, expandedConversations, hiddenLiveTailRange) = conversationView;

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
            idRangesToSkip = conversations
                .Where(c => !expandedConversations.Contains(c.Id))
                .Select(c => c.EntryLidRange)
                .ToArray();
        }
        if (!hiddenLiveTailRange.IsEmpty)
            idRangesToSkip = [..idRangesToSkip, hiddenLiveTailRange];
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
                var lastEntry = entries.Count > 0 ? entries[^1] : null;
                var hasTranscription = lastEntry is { IsContentStreaming: true, AuthorId: var authorId }
                    && authorId == currentAuthorId;
                if (!hasTranscription)
                    entries.Add(audioRecordingEntry);
            }
        }
        if (entries.Count == 0 && conversations.Length == 0)
            return new VirtualListTile<ChatMessage>(lidRange);

        IReadOnlyDictionary<ChatEntryId, string> indexDocIds = ImmutableDictionary<ChatEntryId, string>.Empty;

        var prevEntry = (ChatEntry?)null;
        var prevDate = prevMessage?.Date ?? DateOnly.MinValue;
        var isPrevUnread = false;
        var isPrevAudio = false;
        var hasVeryFirstItem = false;
        if (prevMessage is ChatEntryMessage prevEntryMessage) {
            prevEntry = prevEntryMessage.Entry;
            isPrevUnread = prevMessage.Flags.HasFlag(ChatMessageFlags.Unread);
            isPrevAudio = prevEntry.HasAudio || prevEntry.IsContentStreaming;
            hasVeryFirstItem = prevMessage.Kind == ChatMessageKind.WelcomeBlock;
        }

        var messages = new List<ChatMessage>(entries.Count);
        var items = entries.Merge(conversations.Where(c => !expandedConversations.Contains(c.Id)),
            e => e.LocalId,
            c => c.Id.StartEntryLid);
        var isWelcomeBlockAdded = false;
        foreach (var (entry, conversation) in items) {
            var date = DateOnly.FromDateTime(DateTimeConverter.ToLocalTime(entry?.BeginsAt ?? conversation!.StartsAt));
            if (entry is { IsThreadStart: true }) {
                var threadChatId = entry.ChatId.CreateThreadId(entry.LocalId);
                var threadChat = await Chats.Get(Session, threadChatId, cancellationToken).ConfigureAwait(false);
                if (threadChat is not null) {
                    var message = new ThreadMessage(entry, threadChat) {
                        Date = date,
                        PreviousMessage = prevMessage,
                        Kind = ChatMessageKind.Thread,
                    };
                    messages.Add(message);
                    prevMessage = message;
                }
            }
            else if (entry != null) {
                // Ignore matched conversation
                var isClientMsg = entry.Version == 0;
                var expandedConversation = conversations.FirstOrDefault(c => c.EntryLidRange.Contains(entry.LocalId));
                var isBlockStart = IsBlockStart(prevEntry, entry);
                var isForward = entry.Forwarded is not null;
                var isPrevForward = prevEntry is not null && prevEntry.Forwarded is not null;
                var isForwardFromOtherChat = prevEntry?.Forwarded?.AuthorId.ChatId != entry.Forwarded?.AuthorId.ChatId;
                var isForwardFromOtherAuthor = prevEntry?.Forwarded?.AuthorId != entry.Forwarded?.AuthorId;
                var isForwardBlockStart = (isBlockStart && isForward)
                    || (isForward && (!isPrevForward || isForwardFromOtherChat));
                var isForwardAuthorBlockStart = isForwardBlockStart || (isForward && isForwardFromOtherAuthor);
                var isEntryUnread = entry.LocalId > lastReadEntryId && !isClientMsg;
                var isAudio = entry.HasAudio;
                var shouldAddToResult = lidRange.Contains(entry.LocalId) || entry.IsSending; // add sending entries
                var flags = default(ChatMessageFlags);
                if (isBlockStart)
                    flags |= ChatMessageFlags.BlockStart;
                if ((isBlockStart && isAudio) || isPrevAudio ^ isAudio)
                    flags |= ChatMessageFlags.HasEntryKindSign;
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

                    // Conversation header goes before the date line
                    if (expandedConversation != null && alreadyAddedConversationHeaders.Add(expandedConversation.Id)
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

                    if (expandedConversation != null)
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
            else if (conversation != null && !expandedConversations.Contains(conversation.Id)) {
                var message = new ConversationMessage(conversation) {
                    Kind = ChatMessageKind.ConversationStart,
                    Date = date,
                    PreviousMessage = prevMessage,
                };
                // Can't skip adding a conversation message even if it's the same as previous message
                // Note: the same conversation can be returned by different id tiles as it spans across multiple tiles
                if (prevMessage != null)
                    prevMessage.NextMessage = message;
                messages.Add(message);
                prevMessage = message;
            }
            prevDate = date;
        }
        if (messages.Count > 0 && !lidRange.Contains(messages[0].Id))
            // Remove messages that are outside requested range
            messages.RemoveAll(m =>
                (m is ChatEntryMessage && !lidRange.Contains(m.Id))
                || (m is ConversationMessage cm && lidRange.IntersectWith(cm.Conversation!.EntryLidRange).IsEmpty));
        return new VirtualListTile<ChatMessage>($"tile:{lidRange.Format()}", messages);
    }

    [ComputeMethod]
    public virtual async Task<IReadOnlyList<ChatEntry>> GetThreadPreviewEntries(
        ChatId chatId,
        CancellationToken cancellationToken = default)
        => await Chats.ReadReverse(Session, chatId, cancellationToken)
            .Where(x => !x.IsSystemEntry)
            .Take(2)
            .Reverse()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    [ComputeMethod]
    protected virtual async Task<ChatEntry?> GetAudioRecordingEntry(ChatId chatId, AuthorId ownAuthorId, CancellationToken cancellationToken)
    {
        var state = await Hub.AudioRecorder.State.Use(cancellationToken).ConfigureAwait(false);
        var isRecordingInTheChat = state.ChatId == chatId && state.RecordingStartTime.EpochOffsetTicks > 0;
        if (!isRecordingInTheChat)
            return null;

        var chatEntry = _audioRecordingEntry;
        if (chatEntry?.ChatId == chatId)
            return chatEntry; // Cache chat entry to enable reusing MessageView.

        var entryId = ChatEntryId.New(chatId, long.MaxValue);
        chatEntry = new TextEntry(entryId, 0) {
            AuthorId = ownAuthorId,
            Content = "",
            BeginsAt = state.RecordingStartTime,
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

    private static List<ChatMessage> GroupExpandedConversations(IReadOnlyList<ChatMessage> messages)
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

        foreach (var item in messages) {
            var conversation = item.Conversation;
            var belongs = blockConversation != null
                && (conversation != null
                    ? conversation.Id == blockConversation.Id
                    : blockConversation.EntryLidRange.Contains(item.Id));
            if (belongs) {
                blockItems.Add(item);
                continue;
            }

            FinalizeBlock();
            if (conversation != null && item is not ConversationMessage) {
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

            result.Add(new ExpandedConversationMessage(blockConversation, blockItems));
            blockConversation = null;
            blockItems = [];
        }
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

    // Private methods

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

    // Nested types
    private record AudioRecordingMessageTag;
}
