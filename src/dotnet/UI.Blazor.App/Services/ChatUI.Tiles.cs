using ActualChat.Kvas;
using CommunityToolkit.HighPerformance;
using RangeExt = ActualChat.Mathematics.RangeExt;

namespace ActualChat.UI.Blazor.App.Services;

public record ChatItems(IReadOnlyList<ChatMessage> Items, bool HasBefore, bool HasAfter)
{
    public static readonly ChatItems Empty = new([], false, false);
}

public partial class ChatUI
{
    public const string ShowIndexDocIdChatIdsSettingsKey = "ShowIndexDocIdChatIds";
    private static readonly TimeSpan BlockStartTimeGap = TimeSpan.FromSeconds(120);

    public static readonly TileStack<long> IdTileStack = Constants.Chat.ViewIdTileStack;
    public static readonly TileStack<long> ServerIdTileStack = Constants.Chat.ServerIdTileStack;
    public static readonly int SecondTileSize = (int)IdTileStack.LastLayer.TileSize; // 20

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

        var metaIdTiles = ServerIdTileStack.LastLayer.GetCoveringTiles(dataQuery.ExistingIdRange.Expand(LoadLimit))
            .Where(t => t.Start >= 0)
            .ToList();
        var chatRangeMetaList = (await metaIdTiles
                .Select(metaIdTile
                    => Chats.GetChatRangeMeta(Session, chatId, metaIdTile.Range.Start, cancellationToken))
                .Collect(cancellationToken)
                .ConfigureAwait(false))
            .OrderBy(m => m.IdRange.Start)
            .ThenByDescending(m => m.IdRange.Size()) // ChatRangeMeta can be overlapping, so we need to keep the largest
            .EnsureMonotonic(Comparer<ChatRangeMeta>.Create((a, b) => a.IdRange.Start.CompareTo(b.IdRange.Start)))
            .ToList();

        var showConversations = chat.IsSummarized ?? false;
        if (showConversations && dataQuery.Navigation is { ShouldRestoreViewPosition: false }) {
            var conversationRanges = chatRangeMetaList
                .SelectMany(rm => rm.ConversationIdRanges)
                .EnsureMonotonic(RangeExt.LongRangeComparer)
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

        Range<long> chatIdRange;
        using (Computed.BeginIsolation())
            chatIdRange = await Chats.GetIdRange(Session, chatId, ChatEntryKind.Text, cancellationToken).ConfigureAwait(false);

        List<Range<long>> idTiles;
        bool hasMoreBefore, hasMoreAfter;
        while (!TryGetIdTilesToLoad(dataQuery, chatRangeMetaList, out idTiles, out hasMoreBefore, out hasMoreAfter)) {
            var prevIdTileStart = chatRangeMetaList[0].PreviousIdTileStart;
            var nextIdTileStart = chatRangeMetaList[^1].NextIdTileStart;
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
                    .EnsureMonotonic(RangeExt.LongRangeComparer)
                    .Select(r => Chats.GetTile(Session, chatId, ChatEntryKind.Text, r, cancellationToken))
                    .Collect(ApiConstants.Concurrency.High, cancellationToken);
                var prefetchConversationsTask = showConversations
                    ? idTiles
                        .Select(r => ServerIdTileStack.LastLayer.GetTile(r.Start).Range)
                        .EnsureMonotonic(RangeExt.LongRangeComparer)
                        .Select(r => Conversations.GetTile(Session, chatId, r, cancellationToken))
                        .Collect(ApiConstants.Concurrency.High, cancellationToken)
                    : Task.CompletedTask;
                var prefetchChatInfoTask = PrefetchChatInfo(chatId, cancellationToken);
                await Task.WhenAll(prefetchEntriesTask, prefetchConversationsTask, prefetchChatInfoTask).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
        }

        var chatSendingMessages = Hub.SendingMessages.GetSendingMessages(chatId);
        var chatSendingMessagesWrapper = new IgnoreComputeArg<ChatSendingMessagesAccessor>(chatSendingMessages);
        var isBot = chat.IsAiSearchChat();
        var tiles = new List<VirtualListTile<ChatMessage>>();
        var hasVeryFirstItem = dataQuery.ExistingIdRange.Start + dataQuery.StartOffset <= chatIdRange.Start;
        var prevMessage = hasVeryFirstItem ? ChatMessage.Welcome(chatId, isBot) : null;
        var alreadyAddedConversationHeaders = new HashSet<ConversationId>();
        foreach (var idTile in idTiles) {
            var lastReadEntryLid = shownReadyEntryLid;
            if (lastReadEntryLid < idTile.Start)
                lastReadEntryLid = 0;
            else if (shownReadyEntryLid >= idTile.End - 1)
                lastReadEntryLid = long.MaxValue;
            var isLastTile = idTile.Contains(chatIdRange.End - 1);
            var tile = await GetTile(
                    chatId,
                    chat.Rules.Author?.Id,
                    idTile,
                    showConversations,
                    expandedConversations,
                    prevMessage,
                    lastReadEntryLid,
                    isLastTile ? chatIdRange.End : null,
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

        // Fix NextMessage reference for cached tiles
        if (tiles.Count > 0) {
            for (int i = 0; i < tiles.Count - 1; i++)
                tiles[i].Items[^1].NextMessage = tiles[i + 1].Items[0];
            tiles[^1].Items[^1].NextMessage = null;
        }

        var items = tiles.SelectMany(t => t.Items).ToList();
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
            var hasPreviousIdTile = chatRangeMeta1[0].PreviousIdTileStart.HasValue;
            var hasNextIdTile = chatRangeMeta1[^1].NextIdTileStart.HasValue;
            var entryIdRanges = chatRangeMeta1
                .SelectMany(m => m.EntryIdRanges)
                .EnsureMonotonic(RangeExt.LongRangeComparer);
            var conversationIdRanges = chatRangeMeta1
                .SelectMany(m => m.ConversationIdRanges)
                .EnsureMonotonic(RangeExt.LongRangeComparer);

            var excludedRanges = conversationIdRanges
                .Where(r => !expandedConversations.Contains(ConversationId.New(chatId, r.Start)))
                .ToList();

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
                dataQuery1.ExistingIdRange.Start,
                dataQuery1.StartOffset);

            var endIdWithOffset = GetIdWithOffset(
                resultIdRangesSpan,
                dataQuery1.ExistingIdRange.End,
                dataQuery1.EndOffset);

            var hasFulfilledStart = (startIdWithOffset != null && HasOffsetReached(dataQuery1.StartOffset, startIdWithOffset.Value.ActualOffset)) || !hasPreviousIdTile;
            var hasFulfilledEnd = (endIdWithOffset != null && HasOffsetReached(dataQuery1.EndOffset, endIdWithOffset.Value.ActualOffset)) || !hasNextIdTile;
            var startEntryLid = startIdWithOffset?.Id ?? 0L;
            var endEntryLid = endIdWithOffset?.Id ?? long.MaxValue;
            idTiles1 = resultIdRanges
                .SkipWhile(r => r.End <= startEntryLid)
                .TakeWhile(r => r.Start <= endEntryLid)
                .SelectMany(r => IdTileStack.FirstLayer.GetCoveringTiles(r).Select(t => t.Range))
                .SkipWhile(r => r.End <= startEntryLid)
                .TakeWhile(r => r.Start <= endEntryLid)
                .EnsureMonotonic(RangeExt.LongRangeComparer)
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
        Range<long> idRange,
        bool showConversations,
        IImmutableSet<ConversationId> expandedConversations,
        ChatMessage? prevMessage,
        long lastReadEntryId,
        long? rangeEnd, /* specified only for last tile */
        IgnoreComputeArg<ChatSendingMessagesAccessor> chatSendingMessagesWrapper,
        CancellationToken cancellationToken = default)
    {
        // DebugLog?.LogDebug("GetTile: {ChatId} {IdRange} {LastReadEntryId}", chatId, idRange, lastReadEntryId);
        if (idRange.IsEmptyOrNegative)
            throw new ArgumentOutOfRangeException(nameof(idRange));

        var chatSendingMessages = chatSendingMessagesWrapper.Value;
        var requestedIdRange = prevMessage == null
            ? idRange.MoveStart(-IdTileStack.FirstLayer
                .TileSize) // to request previous item of requested range to properly render block star - we will drop it off
            : idRange;
        var idRangesToSkip = Array.Empty<Range<long>>();
        var conversations = Array.Empty<Conversation>();
        var alreadyAddedConversationHeaders = new HashSet<ConversationId>();
        if (showConversations) {
            var conversationIdTile = ServerIdTileStack.LastLayer.GetTile(idRange.Start); // Get largest tile that contains the requested range
            var conversationTile = await Conversations
                .GetTile(Session, chatId, conversationIdTile.Range, cancellationToken)
                .ConfigureAwait(false);
            conversations = conversationTile
                .Where(c => !c.EntryRange.IntersectWith(requestedIdRange).IsEmpty)
                .ToArray();
            idRangesToSkip = conversations
                .Where(c => !expandedConversations.Contains(c.Id))
                .Select(c => c.EntryRange)
                .ToArray();
        }
        var entryIdTiles = IdTileStack.FirstLayer
            .GetCoveringTiles(requestedIdRange)
            .Where(t => !idRangesToSkip.Any(range => range.Contains(t.Range)))
            .ToList();
        var tiles = await entryIdTiles
            .Select(t => Chats.GetTile(Session,
                chatId,
                ChatEntryKind.Text,
                t.Range,
                cancellationToken))
            .Collect(ApiConstants.Concurrency.High, cancellationToken)
            .ConfigureAwait(false);
        var entries = new List<ChatEntry>();
        foreach (var tile in tiles.OrderBy(t => t.IdTileRange.Start)) {
            foreach (var e in tile.Entries) {
                if (idRangesToSkip.Any(range => range.Contains(e.Id.LocalId)))
                    continue;

                var e2 = await chatSendingMessages.GetSelfOrEdited(e).ConfigureAwait(false);
                entries.Add(e2);
            }
        }

        if (rangeEnd.HasValue) {
            // processing last tile
            chatSendingMessages.RemoveSentNewMessages(rangeEnd.Value);
            var newMessages = await chatSendingMessages.GetNewMessages(currentAuthorId!, rangeEnd.Value).ConfigureAwait(false);
            entries.AddRange(newMessages);
        }
        if (entries.Count == 0 && conversations.Length == 0)
            return new VirtualListTile<ChatMessage>(idRange);

        var showIndexDocId = await GetShowIndexDocId(chatId, cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<ChatEntryId, string> indexDocIds;
        if (showIndexDocId)
            indexDocIds = await GetIndexDocIds(entries, cancellationToken).ConfigureAwait(false);
        else
            indexDocIds = ImmutableDictionary<ChatEntryId, string>.Empty;

        var prevEntry = (ChatEntry?)null;
        var prevDate = prevMessage?.Date ?? DateOnly.MinValue;
        var isPrevUnread = false;
        var isPrevAudio = false;
        var hasVeryFirstItem = false;
        var hasVeryFirstSearchItem = false;
        if (prevMessage is ChatEntryMessage prevEntryMessage) {
            prevEntry = prevEntryMessage.Entry;
            isPrevUnread = prevMessage.Flags.HasFlag(ChatMessageFlags.Unread);
            isPrevAudio = prevEntry.HasAudioEntry || prevEntry.IsStreaming;
            hasVeryFirstItem = prevMessage.ReplacementKind == ChatMessageReplacementKind.WelcomeBlock;
            hasVeryFirstSearchItem = prevMessage.ReplacementKind == ChatMessageReplacementKind.SearchWelcomeBlock;
        }

        var messages = new List<ChatMessage>(entries.Count);
        var items = entries.Merge(conversations.Where(c => !expandedConversations.Contains(c.Id)),
            e => e.LocalId,
            c => c.Id.StartEntryLid);
        var isWelcomeBlockAdded = false;
        foreach (var (entry, conversation) in items) {
            var date = DateOnly.FromDateTime(DateTimeConverter.ToLocalTime(entry?.BeginsAt ?? conversation!.StartsAt));
            if (entry != null && entry.IsThreadStartEntry) {
                var threadChatId = entry.ChatId.CreateThreadId(entry.LocalId);
                var threadChat = await Chats.Get(Session, threadChatId, cancellationToken).ConfigureAwait(false);
                if (threadChat is not null) {
                    var message = new ThreadMessage(entry, threadChat) {
                        Date = date,
                        PreviousMessage = prevMessage,
                    };
                    messages.Add(message);
                    prevMessage = message;
                }
            }
            else if (entry != null) {
                // Ignore matched conversation
                var expandedConversation = conversations.FirstOrDefault(c => c.EntryRange.Contains(entry.LocalId));
                var isBlockStart = IsBlockStart(prevEntry, entry);
                var isForward = entry.ForwardedAuthorId is not null;
                var isPrevForward = prevEntry is not null && prevEntry.ForwardedAuthorId is not null;
                var isForwardFromOtherChat = prevEntry?.ForwardedAuthorId?.ChatId != entry.ForwardedAuthorId?.ChatId;
                var isForwardFromOtherAuthor = prevEntry?.ForwardedAuthorId != entry.ForwardedAuthorId;
                var isForwardBlockStart = (isBlockStart && isForward)
                    || (isForward && (!isPrevForward || isForwardFromOtherChat));
                var isForwardAuthorBlockStart = isForwardBlockStart || (isForward && isForwardFromOtherAuthor);
                var isEntryUnread = entry.LocalId > lastReadEntryId;
                var isAudio = entry.HasAudioEntry;
                var shouldAddToResult = idRange.Contains(entry.LocalId) || entry.IsSending; // add sending entries
                var flags = default(ChatMessageFlags);
                var indexDocId = showIndexDocId ? indexDocIds.GetValueOrDefault(entry.Id, "") : "";
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
                    if (!isWelcomeBlockAdded) {
                        if (hasVeryFirstItem) {
                            var welcomeMessage = new ChatEntryMessage(entry) {
                                ReplacementKind = ChatMessageReplacementKind.WelcomeBlock,
                                ShouldSkipKey = true,
                                PreviousMessage = prevMessage,
                            };
                            if (prevMessage != null)
                                prevMessage.NextMessage = welcomeMessage;
                            messages.Add(welcomeMessage);
                            prevMessage = welcomeMessage;
                        }
                        if (hasVeryFirstSearchItem) {
                            var welcomeMessage = new ChatEntryMessage(entry) {
                                ReplacementKind = ChatMessageReplacementKind.SearchWelcomeBlock,
                                ShouldSkipKey = true,
                                PreviousMessage = prevMessage,
                            };
                            if (prevMessage != null)
                                prevMessage.NextMessage = welcomeMessage;
                            messages.Add(welcomeMessage);
                            prevMessage = welcomeMessage;
                        }
                        isWelcomeBlockAdded = true;
                    }

                    if (isEntryUnread && !isPrevUnread) {
                        var newLineMessage = new ChatEntryMessage(entry) {
                            ReplacementKind = ChatMessageReplacementKind.NewMessagesLine,
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
                        && (prevMessage == null || prevMessage.Id < expandedConversation.Id.StartEntryLid)) {
                        // Add a conversation header only if it wasn't added before
                        var conversationHeaderMessage = new ConversationHeader(expandedConversation) {
                            ReplacementKind = ChatMessageReplacementKind.ConversationStart,
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
                            ReplacementKind = ChatMessageReplacementKind.DateLine,
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
                    var message = new ChatEntryMessage(entry) {
                        Date = date,
                        Flags = flags,
                        PreviousMessage = prevMessage,
                        ShowIndexDocId = showIndexDocId,
                        IndexDocId = indexDocId,
                        Conversation = expandedConversation,
                    };
                    if (prevMessage != null)
                        prevMessage.NextMessage = message;
                    messages.Add(message);
                    prevMessage = message;

                    if (expandedConversation != null)
                        if (entry.Id.LocalId == expandedConversation.EndEntryLid) {
                            var conversationFooterMessage = new ConversationFooter(expandedConversation) {
                                ReplacementKind = ChatMessageReplacementKind.ConversationEnd,
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
                    ReplacementKind = ChatMessageReplacementKind.ConversationStart,
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
        if (messages.Count > 0 && !idRange.Contains(messages[0].Id))
            // Remove messages that are outside requested range
            messages.RemoveAll(m =>
                (m is ChatEntryMessage && !idRange.Contains(m.Id))
                || (m is ConversationMessage cm && idRange.IntersectWith(cm.Conversation!.EntryRange).IsEmpty));
        return new VirtualListTile<ChatMessage>($"tile:{idRange.Format()}", messages);
    }

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

            result.Add(groupedItems.Count == 1
                ? groupedItems[0]
                : new ChatEntryAuthorGroup(groupedItems[0].Entry.AuthorId, groupedItems) { Conversation = groupedItems[0].Conversation });
        }
    }

    private static List<ChatMessage> GroupExpandedConversations(IReadOnlyList<ChatMessage> messages)
    {
        var result = new List<ChatMessage>();
        var groupedItems = new List<ChatMessage>();
        Conversation? ongoingConversation = null;
        var ongoingConversationItems = new List<ChatMessage>();

        foreach (var item in messages)
            if (item.Conversation == null || item is ConversationMessage) {
                FinalizeOngoingConversation();
                groupedItems.Add(item);
            }
            else {
                if (ongoingConversation != null && ongoingConversation.Id != item.Conversation.Id)
                    FinalizeOngoingConversation();

                ongoingConversation = item.Conversation;
                ongoingConversationItems.Add(item);
            }

        FinalizeOngoingConversation();
        result.AddRange(groupedItems);
        return result;

        void FinalizeOngoingConversation()
        {
            if (ongoingConversation == null)
                return;

            groupedItems.Add(new ExpandedConversationMessage(ongoingConversation, ongoingConversationItems));
            ongoingConversation = null;
            ongoingConversationItems = [];
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
                var idRangeTask = Chats.GetIdRange(Session, chatId, ChatEntryKind.Text, cancellationToken);
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

    private async Task<bool> GetShowIndexDocId(ChatId chatId, CancellationToken cancellationToken)
    {
        var account = AccountUI.OwnAccount.Value;
        if (!account.IsAdmin)
            return false;

        var chatIdListToShowIndexDocId = await Hub.AccountSettings
            .Get<string>(ShowIndexDocIdChatIdsSettingsKey, cancellationToken)
            .ConfigureAwait(false);
        var chatSidsShowIndexDocId = chatIdListToShowIndexDocId?.Split(';') ?? [];
        var showIndexDocId = chatSidsShowIndexDocId.Contains(chatId.Value, StringComparer.Ordinal);
        return showIndexDocId;
    }

    private async Task<IReadOnlyDictionary<ChatEntryId, string>> GetIndexDocIds(
        List<ChatEntry> entries,
        CancellationToken cancellationToken)
    {
        using (Computed.BeginIsolation()) {
            var entryIds = entries.Select(x => x.Id).ToList();
            var docIds = await entryIds
                .Select(c => MLSearch.GetIndexDocIdByEntryId(Session, c, cancellationToken))
                .Collect(ApiConstants.Concurrency.High, cancellationToken)
                .ConfigureAwait(false);
            var result = entryIds
                .Zip(docIds, (entryId, docId) => (entryId, docId))
                .ToDictionary(c => c.entryId, c => c.docId);
            return result;
        }
    }

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
}
