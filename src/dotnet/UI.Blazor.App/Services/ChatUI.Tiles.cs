using ActualChat.Kvas;

namespace ActualChat.UI.Blazor.App.Services;

public partial class ChatUI
{
    public const string ShowIndexDocIdChatIdsSettingsKey = "ShowIndexDocIdChatIds";
    private static readonly TimeSpan BlockStartTimeGap = TimeSpan.FromSeconds(120);

    // NOTE: Please don't add excessive computed dependencies without real reason - it might rerender whole chat view content
    [ComputeMethod(MinCacheDuration = 30, InvalidationDelay = 0.1)]
    public virtual async Task<VirtualListTile<ChatMessage>> GetTile(
        ChatId chatId,
        Range<long> idRange,
        ChatMessage? prevMessage,
        long lastReadEntryId,
        CancellationToken cancellationToken = default)
    {
        if (idRange.IsEmptyOrNegative)
            throw new ArgumentOutOfRangeException(nameof(idRange));

        var requestedIdRange = prevMessage == null
            ? idRange.MoveStart(-1) // to request previous item of requested range to properly render block star - we will drop it off
            : idRange;
        var tiles = await ChatView.IdTileStack.FirstLayer
            .GetCoveringTiles(requestedIdRange)
            .Select(t => Chats.GetTile(Session, chatId, ChatEntryKind.Text, t.Range, cancellationToken))
            .Collect()
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
        if (prevMessage != null) {
            prevEntry = prevMessage.Entry;
            prevDate = DateOnly.FromDateTime(DateTimeConverter.ToLocalTime(prevEntry.BeginsAt));
            isPrevUnread = prevMessage.Flags.HasFlag(ChatMessageFlags.Unread);
            isPrevAudio = prevEntry.HasAudioEntry || prevEntry.IsStreaming;
            hasVeryFirstItem = prevMessage.ReplacementKind == ChatMessageReplacementKind.WelcomeBlock;
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
            if (shouldAddToResult) {
                if (hasVeryFirstItem && !isWelcomeBlockAdded) {
                    var welcomeMessage = new ChatMessage(entry) {
                        ReplacementKind = ChatMessageReplacementKind.WelcomeBlock,
                        PreviousMessage = prevMessage,
                    };
                    messages.Add(welcomeMessage);
                    prevMessage = welcomeMessage;
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

    private async Task<bool> GetShowIndexDocId(ChatId chatId, CancellationToken cancellationToken)
    {
        var account = AccountUI.OwnAccount.Value;
        var chatIdListToShowIndexDocId = await Hub.AccountSettings().Get<string>(ShowIndexDocIdChatIdsSettingsKey, cancellationToken).ConfigureAwait(false);
        var chatSidsShowIndexDocId = chatIdListToShowIndexDocId?.Split(';') ?? [];
        var showIndexDocId = account.IsAdmin && !chatId.IsNone && chatSidsShowIndexDocId.Contains(chatId.Value, StringComparer.Ordinal);
        return showIndexDocId;
    }

    private async Task<IReadOnlyDictionary<ChatEntryId, string>> GetIndexDocIds(List<ChatEntry> entries, CancellationToken cancellationToken)
    {
        using (Computed.BeginIsolation()) {
            var entryIds = entries.Select(x => x.Id).ToList();
            var docIds = await entryIds
                .Select(c => MLSearch.GetIndexDocIdByEntryId(Session, c, cancellationToken))
                .Collect()
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
