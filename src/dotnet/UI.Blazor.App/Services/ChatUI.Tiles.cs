using ActualChat.Kvas;

namespace ActualChat.UI.Blazor.App.Services;

public partial class ChatUI
{
    public const string ShowIndexDocIdChatIdsSettingsKey = "ShowIndexDocIdChatIds";
    private static readonly TimeSpan BlockStartTimeGap = TimeSpan.FromSeconds(120);

    public static readonly TileStack<long> IdTileStack = Constants.Chat.ViewIdTileStack;
    public static readonly long SecondTileSize = IdTileStack.Layers[1].TileSize; // 20
    public static readonly long HalfLoadLimit = SecondTileSize; // 20
    public static readonly long LoadLimit = 2 * SecondTileSize; // 40

    public async Task<List<VirtualListTile<ChatMessage>>> GetTiles(
        ChatId chatId,
        Range<long> idRange,
        long shownReadyEntryLid,
        CancellationToken cancellationToken)
    {
        DebugLog?.LogDebug("GetTiles: {ChatId} {IdRange} {ShownReadyEntryLid}", chatId, idRange, shownReadyEntryLid);
        var chat = await Chats.Get(Session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return [];

        var chatIdRange = await Chats.GetIdRange(Session, chatId, ChatEntryKind.Text, cancellationToken).ConfigureAwait(false);
        var idTiles = GetIdTilesToLoad(idRange, chatIdRange);
        var isBot = chat.IsAiSearchChat();
        var hasVeryFirstItem = idRange.Start <= chatIdRange.Start;
        var prevMessage = hasVeryFirstItem ? ChatMessage.Welcome(chatId, isBot) : null;
        var tiles = new List<VirtualListTile<ChatMessage>>();
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
                prevMessage,
                lastReadEntryLid,
                cancellationToken);
            if (tile.Items.Count == 0)
                continue;

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
        return tiles;
    }

    // NOTE: Please don't add excessive computed dependencies without real reason - it might rerender whole chat view content
    [ComputeMethod(MinCacheDuration = 30, InvalidationDelay = 0.1)]
    public virtual async Task<VirtualListTile<ChatMessage>> GetTile(
        ChatId chatId,
        AuthorId currentAuthorId,
        Range<long> idRange,
        ChatMessage? prevMessage,
        long lastReadEntryId,
        CancellationToken cancellationToken = default)
    {
        DebugLog?.LogDebug("GetTile: {ChatId} {IdRange} {LastReadEntryId}", chatId, idRange, lastReadEntryId);
        if (idRange.IsEmptyOrNegative)
            throw new ArgumentOutOfRangeException(nameof(idRange));

        var requestedIdRange = prevMessage == null
            ? idRange.MoveStart(-1) // to request previous item of requested range to properly render block star - we will drop it off
            : idRange;
        var tiles = await IdTileStack.FirstLayer
            .GetCoveringTiles(requestedIdRange)
            .Select(t => Chats.GetTile(Session, chatId, ChatEntryKind.Text, t.Range, cancellationToken))
            .Collect(ApiConstants.Concurrency.High, cancellationToken)
            .ConfigureAwait(false);
        var entries = tiles.SelectMany(t => t.Entries).ToList();
        if (entries.Count == 0)
            return new VirtualListTile<ChatMessage>(idRange);

        var showIndexDocId = await GetShowIndexDocId(chatId, cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<ChatEntryId, string> indexDocIds;
        if (showIndexDocId)
            indexDocIds = await GetIndexDocIds(entries, cancellationToken).ConfigureAwait(false);
        else
            indexDocIds = ImmutableDictionary<ChatEntryId, string>.Empty;

        var prevEntry = (ChatEntry?)null;
        var prevDate = DateOnly.MinValue;
        var isPrevUnread = false;
        var isPrevAudio = false;
        var hasVeryFirstItem = false;
        var hasVeryFirstSearchItem = false;
        if (prevMessage != null) {
            prevEntry = prevMessage.Entry;
            prevDate = DateOnly.FromDateTime(DateTimeConverter.ToLocalTime(prevEntry.BeginsAt));
            isPrevUnread = prevMessage.Flags.HasFlag(ChatMessageFlags.Unread);
            isPrevAudio = prevEntry.HasAudioEntry || prevEntry.IsStreaming;
            hasVeryFirstItem = prevMessage.ReplacementKind == ChatMessageReplacementKind.WelcomeBlock;
            hasVeryFirstSearchItem = prevMessage.ReplacementKind == ChatMessageReplacementKind.SearchWelcomeBlock;
        }

        var messages = new List<ChatMessage>(entries.Count);
        var isWelcomeBlockAdded = false;
        foreach (var entry in entries) {
            var date = DateOnly.FromDateTime(DateTimeConverter.ToLocalTime(entry.BeginsAt));
            var isBlockStart = IsBlockStart(prevEntry, entry);
            var isForward = !entry.ForwardedAuthorId.IsNone;
            var isPrevForward = prevEntry is { ForwardedAuthorId.IsNone: false };
            var isForwardFromOtherChat = prevEntry?.ForwardedAuthorId.ChatId != entry.ForwardedAuthorId.ChatId;
            var isForwardFromOtherAuthor = prevEntry?.ForwardedAuthorId != entry.ForwardedAuthorId;
            var isForwardBlockStart = (isBlockStart && isForward) || (isForward && (!isPrevForward || isForwardFromOtherChat));
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
                        var welcomeMessage = new ChatMessage(entry) {
                            ReplacementKind = ChatMessageReplacementKind.WelcomeBlock,
                            PreviousMessage = prevMessage,
                        };
                        messages.Add(welcomeMessage);
                        prevMessage = welcomeMessage;
                    }
                    if (hasVeryFirstSearchItem) {
                        var welcomeMessage = new ChatMessage(entry) {
                            ReplacementKind = ChatMessageReplacementKind.SearchWelcomeBlock,
                            PreviousMessage = prevMessage,
                        };
                        messages.Add(welcomeMessage);
                        prevMessage = welcomeMessage;
                    }
                    isWelcomeBlockAdded = true;
                }

                if (isEntryUnread && !isPrevUnread) {
                    var newLineMessage = new ChatMessage(entry) {
                        ReplacementKind = ChatMessageReplacementKind.NewMessagesLine,
                        PreviousMessage = prevMessage,
                    };
                    messages.Add(newLineMessage);
                    prevMessage = newLineMessage;
                }
                if (date != prevDate) {
                    var dateLineMessage = new ChatMessage(entry) {
                        ReplacementKind = ChatMessageReplacementKind.DateLine,
                        Date = date,
                        PreviousMessage = prevMessage,
                    };
                    messages.Add(dateLineMessage);
                    prevMessage = dateLineMessage;
                }
                var message = new ChatMessage(entry) {
                    Date = date,
                    Flags = flags,
                    PreviousMessage = prevMessage,
                    ShowIndexDocId = showIndexDocId,
                    IndexDocId = indexDocId
                };
                messages.Add(message);
                prevMessage = message;
            }
            prevEntry = entry;
            prevDate = date;
            isPrevUnread = isEntryUnread;
            isPrevAudio = isAudio;
        }
        return new VirtualListTile<ChatMessage>($"tile:{idRange.Format()}", messages);
    }

    [ComputeMethod]
    public virtual ValueTask<ChatEntry?> GetEntry(
        ChatEntryId id,
        CancellationToken cancellationToken = default)
        => Chats.GetEntry(Session, id, cancellationToken);

    public Task PrefetchTiles(ChatId chatId, Range<long> idRange, CancellationToken cancellationToken)
    {
        if (idRange.IsEmptyOrNegative)
            return Task.CompletedTask;

        DebugLog?.LogDebug("PrefetchTiles: {ChatId} {IdRange}", chatId, idRange);

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
                .Collect(ApiConstants.Concurrency.High, cancellationToken);
            await Task.WhenAll(chatTask, idRangeTask, rulesTask, authorsTask, isEmptyTask, tilesTask).ConfigureAwait(false);
        }, Log, "Error prefetching chat tiles.", CancellationToken.None);
    }

    // Private methods

    private Tile<long>[] GetIdTilesToLoad(Range<long> idRangeToLoad, Range<long> chatIdRange)
    {
        DebugLog?.LogDebug("GetIdTilesToLoad: {IdRangeToLoad} {ChatIdRange}", idRangeToLoad, chatIdRange);
        idRangeToLoad = new Range<long>(Math.Max(chatIdRange.Start, idRangeToLoad.Start), idRangeToLoad.End);
        var firstLayer = IdTileStack.FirstLayer;
        var secondLayer = IdTileStack.Layers[1];
        var tiles = ArrayBuffer<Tile<long>>.Lease(true);
        try {
            // hot range assumes high probability of changes - so close to the end of the chat messages
            var hotRangeTiles = firstLayer.GetCoveringTiles(new Range<long>(chatIdRange.End - secondLayer.TileSize, chatIdRange.End + firstLayer.TileSize));
            var hotRange = new Range<long>(hotRangeTiles[0].Range.Start, hotRangeTiles[^1].Range.End);
            if (!idRangeToLoad.Overlaps(hotRange)) // idRangeToLoad has already been extended to cover ids beyond existing chat id range
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

        var chatIdListToShowIndexDocId = await Hub.AccountSettings().Get<string>(ShowIndexDocIdChatIdsSettingsKey, cancellationToken).ConfigureAwait(false);
        var chatSidsShowIndexDocId = chatIdListToShowIndexDocId?.Split(';') ?? [];
        var showIndexDocId = chatSidsShowIndexDocId.Contains(chatId.Value, StringComparer.Ordinal);
        return showIndexDocId;
    }

    private async Task<IReadOnlyDictionary<ChatEntryId, string>> GetIndexDocIds(List<ChatEntry> entries, CancellationToken cancellationToken)
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
