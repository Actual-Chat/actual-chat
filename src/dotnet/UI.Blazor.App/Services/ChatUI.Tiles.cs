using ActualChat.Kvas;

namespace ActualChat.UI.Blazor.App.Services;

public partial class ChatUI
{
    public const string ShowIndexDocIdChatIdsSettingsKey = "ShowIndexDocIdChatIds";
    private static readonly TimeSpan BlockStartTimeGap = TimeSpan.FromSeconds(120);

    public static readonly TileStack<long> IdTileStack = Constants.Chat.ViewIdTileStack;
    public static readonly TileStack<long> ConversationTileStack = Constants.Chat.ConversationTileStack;
    public static readonly int SecondTileSize = (int)IdTileStack.Layers[1].TileSize; // 20

    private IImmutableSet<ConversationId> LastExpandedConversations { get; set; } =
        ImmutableHashSet<ConversationId>.Empty;

    public int HalfLoadLimit => BrowserInfo.IsMobile ? SecondTileSize : SecondTileSize * 2; // 20 for mobile
    public int LoadLimit => BrowserInfo.IsMobile ? SecondTileSize * 2 : SecondTileSize * 4; // 40 for mobile

    public async Task<IReadOnlyList<ChatMessage>> GetChatItems(
        ChatId chatId,
        ChatDataQuery dataQuery,
        long shownReadyEntryLid,
        CancellationToken cancellationToken)
    {
        // DebugLog?.LogDebug("GetTiles: {ChatId} {IdRange} {ShownReadyEntryLid}", chatId, dataQuery, shownReadyEntryLid);
        var chat = await Chats.Get(Session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return [];

        var showConversations = chat.IsSummarized ?? false;
        IImmutableSet<ConversationId> expandedConversations = ImmutableHashSet<ConversationId>.Empty;
        if (showConversations) {
            expandedConversations = await ExpandedConversations.Use(cancellationToken).ConfigureAwait(false);
            var changedExpand = expandedConversations.SymmetricExcept(LastExpandedConversations)
                .OrderByDescending(c => dataQuery.IdRange.Contains(c.StartEntryLid))
                .ToList();
            LastExpandedConversations = expandedConversations;
            if (changedExpand.Count > 0) {
                // Adjust data query to load tiles around expanded conversation entries
                var conversationId = changedExpand.FirstOrDefault();
                var conversation = await Conversations.Get(Session, conversationId, cancellationToken)
                    .ConfigureAwait(false);
                var isExpanded = expandedConversations.Contains(conversationId);
                var loadBefore = isExpanded ? HalfLoadLimit : SecondTileSize;
                var loadAfter = isExpanded ? 0 : HalfLoadLimit + SecondTileSize;
                var keyRange = new Range<long>(
                    Math.Min(dataQuery.IdRange.Start, conversationId.StartEntryLid),
                    Math.Max(dataQuery.IdRange.End, conversation.Require().EndEntryLid));
                dataQuery = new ChatDataQuery(keyRange, loadBefore, loadAfter);
            }
        }

        var originalLoadBefore = dataQuery.LoadBefore;
        var originalLoadAfter = dataQuery.LoadAfter;
        var isBot = chat.IsAiSearchChat();
        var tiles = new List<VirtualListTile<ChatMessage>>();
        while (true) {
            var idTiles = GetIdTilesToLoad(dataQuery);
            var prevMessage = dataQuery.HasVeryFirstItem ? ChatMessage.Welcome(chatId, isBot) : null;
            var alreadyAddedConversationHeaders = new HashSet<ConversationId>();
            foreach (var idTile in idTiles) {
                var lastReadEntryLid = shownReadyEntryLid;
                if (lastReadEntryLid < idTile.Range.Start)
                    lastReadEntryLid = 0;
                else if (shownReadyEntryLid >= idTile.Range.End - 1)
                    lastReadEntryLid = long.MaxValue;
                var tile = await GetTile(
                        chatId,
                        chat.Rules.Author?.Id ?? AuthorId.None,
                        idTile.Range,
                        showConversations,
                        expandedConversations,
                        prevMessage,
                        lastReadEntryLid,
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
                    // Skip first conversation header if already added
                    if (tile.Items[0] is ConversationHeader conversationHeader
                        && alreadyAddedConversationHeaders.Contains(conversationHeader.Conversation!.Id))
                        tile = tile with { Items = tile.Items.Skip(1).ToList() };

                if (tile.Items.Count == 0)
                    continue;

                if (tile.Items[0].Equals(prevMessage)) {
                    // Skip first item if it's the same as previous tile - e.g. large conversation that spans across multiple tiles
                    tile = tile with { Items = tile.Items.Skip(1).ToList() };
                    if (tile.Items.Count == 0)
                        continue;
                }

                tiles.Add(tile);
                prevMessage = tile.Items[^1];
#if false
        // Uncomment for debugging:
        DebugLog?.LogDebug("Tile: #{IdRange}, {IsUnread}, {LastReadEntryLid}",
            idTile.Range.Format(), isUnread, lastReadEntryLidArg);
        foreach (var item in tile.Items)
            DebugLog?.LogDebug("- {Key}: {ReplacementKind}", item.Key, item.ReplacementKind);
#endif
            }
            if (originalLoadBefore == 0 && originalLoadAfter == 0)
                break;

            var expandedDataQuery = await ExpandDataQuery(chatId,
                    dataQuery,
                    originalLoadBefore,
                    originalLoadAfter,
                    tiles.SelectMany(t => t.Items),
                    cancellationToken)
                .ConfigureAwait(false);
            if (expandedDataQuery == null)
                break;

            dataQuery = expandedDataQuery;
            tiles.Clear();
        }

        // Fix NextMessage reference for cached tiles
        if (tiles.Count > 0) {
            for (int i = 0; i < tiles.Count - 1; i++)
                tiles[i].Items[^1].NextMessage = tiles[i + 1].Items[0];
            tiles[^1].Items[^1].NextMessage = null;
        }

        var items = tiles.SelectMany(t => t.Items);
        var groupedItems = GroupAuthorMessages(items);

        if (expandedConversations.Count == 0)
            return groupedItems;

        var groupedTiles = GroupExpandedConversations(groupedItems);
        return groupedTiles;
    }

    [ComputeMethod]
    public virtual ValueTask<ChatEntry?> GetEntry(
        ChatEntryId id,
        CancellationToken cancellationToken = default)
        => Chats.GetEntry(Session, id, cancellationToken);

    public async Task<ChatDataQuery?> ExpandDataQuery(
        ChatId chatId,
        ChatDataQuery dataQuery,
        int originalLoadBefore,
        int originalLoadAfter,
        IEnumerable<ChatMessage> items,
        CancellationToken cancellationToken)
    {
        var (beforeCount, afterCount) = GetLoadedBeforeAndAfterCounts(dataQuery, items);
        var hasBeforeOrAfter = originalLoadBefore > 0 || originalLoadAfter > 0;
        var beforeFulfilled = hasBeforeOrAfter && beforeCount >= originalLoadBefore / 2;
        var afterFulfilled = hasBeforeOrAfter && afterCount >= originalLoadAfter / 2;
        if (beforeFulfilled && afterFulfilled)
            return null;

        var chatIdRange = await Chats.GetIdRange(Session, chatId, ChatEntryKind.Text, cancellationToken)
            .ConfigureAwait(false);
        var hasVeryFirstItem = chatIdRange.Start >= dataQuery.Start;
        var hasVeryLastItem = chatIdRange.End <= dataQuery.End;
        if (hasVeryFirstItem && hasVeryLastItem)
            return null;

        if (beforeFulfilled && hasVeryLastItem)
            return null;

        if (afterFulfilled && hasVeryFirstItem)
            return null;

        // Expand load limits and reset tiles if we need to load more just to fulfill one side
        var expandedLoadBefore = hasVeryFirstItem || beforeFulfilled
            ? dataQuery.LoadBefore
            : Math.Max(dataQuery.LoadBefore * 4, HalfLoadLimit);
        var expandedLoadAfter = hasVeryLastItem || afterFulfilled
            ? dataQuery.LoadAfter
            : Math.Max(dataQuery.LoadAfter * 4, HalfLoadLimit);
        return new ChatDataQuery(dataQuery.IdRange, expandedLoadBefore, expandedLoadAfter) {
            HasVeryFirstItem = hasVeryFirstItem,
            HasVeryLastItem = hasVeryLastItem,
        };
    }

    // NOTE: Please don't add excessive computed dependencies without real reason - it might rerender whole chat view content
    [ComputeMethod(MinCacheDuration = 30, InvalidationDelay = 0.1)]
    protected virtual async Task<VirtualListTile<ChatMessage>> GetTile(
        ChatId chatId,
        AuthorId currentAuthorId,
        Range<long> idRange,
        bool showConversations,
        IImmutableSet<ConversationId> expandedConversations,
        ChatMessage? prevMessage,
        long lastReadEntryId,
        CancellationToken cancellationToken = default)
    {
        // DebugLog?.LogDebug("GetTile: {ChatId} {IdRange} {LastReadEntryId}", chatId, idRange, lastReadEntryId);
        if (idRange.IsEmptyOrNegative)
            throw new ArgumentOutOfRangeException(nameof(idRange));

        var requestedIdRange = prevMessage == null
            ? idRange.MoveStart(-IdTileStack.FirstLayer
                .TileSize) // to request previous item of requested range to properly render block star - we will drop it off
            : idRange;
        var idRangesToSkip = Array.Empty<Range<long>>();
        var conversations = Array.Empty<Conversation>();
        var alreadyAddedConversationHeaders = new HashSet<ConversationId>();
        if (showConversations) {
            var conversationIdTile =
                ConversationTileStack.LastLayer
                    .GetTile(idRange.Start); // Get largest tile that contains the requested range
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
        var entries = tiles
            .OrderBy(t => t.IdTileRange.Start)
            .SelectMany(t => t.Entries)
            .Where(e => !idRangesToSkip.Any(range => range.Contains(e.Id.LocalId)))
            .ToList();
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
            (entry, conversation) => (int)(entry.Id.LocalId - conversation.Id.StartEntryLid));
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
                var isForward = !entry.ForwardedAuthorId.IsNone;
                var isPrevForward = prevEntry is { ForwardedAuthorId.IsNone: false };
                var isForwardFromOtherChat = prevEntry?.ForwardedAuthorId.ChatId != entry.ForwardedAuthorId.ChatId;
                var isForwardFromOtherAuthor = prevEntry?.ForwardedAuthorId != entry.ForwardedAuthorId;
                var isForwardBlockStart = (isBlockStart && isForward)
                    || (isForward && (!isPrevForward || isForwardFromOtherChat));
                var isForwardAuthorBlockStart = isForwardBlockStart || (isForward && isForwardFromOtherAuthor);
                var isEntryUnread = entry.LocalId > lastReadEntryId;
                var isAudio = entry.HasAudioEntry;
                var shouldAddToResult = idRange.Contains(entry.LocalId);
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
                    if (expandedConversation != null && alreadyAddedConversationHeaders.Add(expandedConversation.Id)) {
                        var conversationHeaderMessage = new ConversationHeader(expandedConversation) {
                            ReplacementKind = ChatMessageReplacementKind.ConversationStart,
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
                // Can't skip adding conversation message even if it's the same as previous message
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
                if (ongoingConversation != null && ongoingConversation != item.Conversation)
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

    private (int, int) GetLoadedBeforeAndAfterCounts(ChatDataQuery dataQuery, IEnumerable<ChatMessage> items)
    {
        var before = 0;
        var after = 0;
        foreach (var item in items)
            if (item is IVirtualListGroup<ChatMessage> group)
                foreach (var nestedItem in group.Items)
                    UpdateCounts(nestedItem);
            else
                UpdateCounts(item);
        return (before, after);

        void UpdateCounts(ChatMessage item)
        {
            if (item.Id < dataQuery.IdRange.Start)
                before++;
            else if (item.Id > dataQuery.IdRange.End)
                after++;
        }
    }

    private Task PrefetchTiles(ChatId chatId, Range<long> idRange, CancellationToken cancellationToken)
    {
        if (idRange.IsEmptyOrNegative)
            return Task.CompletedTask;

        // DebugLog?.LogDebug("PrefetchTiles: {ChatId} {IdRange}", chatId, idRange);

        return BackgroundTask.Run(async () => {
                // We are making following calls during chat view rendering:
                // IChats.Get:3
                // IChats.GetTile:3
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
                var tilesTask = IdTileStack.FirstLayer
                    .GetCoveringTiles(idRange)
                    .Select(x => Chats.GetTile(Session,
                        chatId,
                        ChatEntryKind.Text,
                        x.Range,
                        cancellationToken))
                    .Collect(ApiConstants.Concurrency.High, cancellationToken);

                var tiles = await tilesTask.ConfigureAwait(false);

                // prefetch authors
                await tiles
                    .SelectMany(t => t.Entries)
                    .Select(e => e.AuthorId)
                    .Distinct()
                    .Select(authorId => Authors.Get(Session, chatId, authorId, cancellationToken))
                    .Collect(ApiConstants.Concurrency.High, cancellationToken)
                    .ConfigureAwait(false);
                await Task.WhenAll(chatTask,
                        idRangeTask,
                        rulesTask,
                        authorsTask,
                        isEmptyTask,
                        tilesTask)
                    .ConfigureAwait(false);
            },
            Log,
            "Error prefetching chat tiles.",
            CancellationToken.None);
    }

    // Private methods

    private Tile<long>[] GetIdTilesToLoad(ChatDataQuery dataQuery)
    {
        // DebugLog?.LogDebug("GetIdTilesToLoad: {ChatDataQuery}", dataQuery);
        var idRangeToLoad = new Range<long>(dataQuery.Start, dataQuery.End);
        var firstLayer = IdTileStack.FirstLayer;
        var secondLayer = IdTileStack.Layers[1];
        var tiles = ArrayBuffer<Tile<long>>.Lease(true);
        try {
            // hot range assumes high probability of changes - so close to the end of the chat messages
            var hotRangeTiles = dataQuery.HasVeryLastItem
                ? firstLayer.GetCoveringTiles(new Range<long>(idRangeToLoad.End - secondLayer.TileSize,
                    idRangeToLoad.End + firstLayer.TileSize))
                : [];
            var hotRange = hotRangeTiles.Length > 0
                ? new Range<long>(hotRangeTiles[0].Range.Start, hotRangeTiles[^1].Range.End)
                : default;
            if (!idRangeToLoad
                    .Overlaps(hotRange)) // idRangeToLoad has already been extended to cover ids beyond existing chat id range
                hotRange = default;

            var coldRange = hotRange.IsEmpty
                ? idRangeToLoad
                : new Range<long>(secondLayer.GetTile(idRangeToLoad.Start).Start, hotRange.Start);

            // load second layer stack to improve reuse if large tiles during scroll
            tiles.AddRange(secondLayer.GetCoveringTiles(coldRange));
            var lastColdRange = tiles.Count > 0
                ? tiles[^1].Range
                : default;
            tiles.AddRange(firstLayer.GetCoveringTiles(hotRange).SkipWhile(hr => hr.Range.Overlaps(lastColdRange)));
            var result = tiles.ToArray();
            // if (result.DistinctBy(x => x.Range).Count() != result.Length)
            //     Debugger.Break();
            return result;
        }
        finally {
            tiles.Release();
        }
    }

    private async Task<bool> GetShowIndexDocId(ChatId chatId, CancellationToken cancellationToken)
    {
        var account = AccountUI.OwnAccount.Value;
        if (!account.IsAdmin || chatId.IsNone)
            return false;

        var chatIdListToShowIndexDocId = await Hub.AccountSettings()
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
}
