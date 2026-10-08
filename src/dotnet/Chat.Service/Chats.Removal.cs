using ActualChat.Time;

namespace ActualChat.Chat;

public partial class Chats
{
    public virtual async Task<ApiArray<ChatId>> ListOwnSoleOwnedChatIds(
        Session session, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account.IsGuest)
            return ApiArray<ChatId>.Empty;

        return await Backend.ListSoleOwnedChatIds(account.Id, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<Range<long>> OnWipeHistory(
        Chats_WipeHistory command, CancellationToken cancellationToken)
    {
        var session = command.Session;
        var chat = await Get(session, command.ChatId, cancellationToken).Require().ConfigureAwait(false);
        chat.Rules.Permissions.Require(GetHistoryPermissions(chat.Id));
        ThrowIfPlaceRootChat(chat.Id);
        await MaintenancesBackend.RequireAvailable(chat.Id, cancellationToken).ConfigureAwait(false);

        var entryLidRange = new Range<long>(command.MinEntryLid, long.MaxValue);
        var requestHistoryWipeCmd = new ChatsBackend_RequestHistoryWipe(chat.Id, entryLidRange);
        var wipedRange = await Commander.Call(requestHistoryWipeCmd, cancellationToken).ConfigureAwait(false);
        if (wipedRange.IsEmpty)
            return wipedRange;

        // The system entry says how far back the wipe went from now; none = the whole history
        TimeSpan? period = null;
        if (wipedRange.Start != 0) {
            var firstEntryId = ChatEntryId.New(chat.Id, wipedRange.Start);
            var firstEntry = await Backend.GetEntry(firstEntryId, cancellationToken).ConfigureAwait(false);
            if (firstEntry is not null)
                period = (Clocks.SystemClock.Now - firstEntry.BeginsAt).CeilingToPeriodUnit();
        }
        await PostHistoryChangedEntry(session, chat.Id, HistoryChangeKind.Wiped, period, cancellationToken)
            .ConfigureAwait(false);
        return wipedRange;
    }

    // Private methods

    // Either side of a peer chat manages its history, as there's no owner there
    private static ChatPermissions GetHistoryPermissions(ChatId chatId)
        => chatId is PeerChatId ? ChatPermissions.EditProperties : ChatPermissions.Owner;

    private async Task PostHistoryChangedEntry(
        Session session, ChatId chatId, HistoryChangeKind change, TimeSpan? period,
        CancellationToken cancellationToken)
    {
        var author = await Authors.GetOwn(session, chatId, cancellationToken).ConfigureAwait(false);
        var authorId = author is null || author.IsAnonymous ? null : author.Id;
        var authorName = authorId is null ? "" : author?.Avatar.Name ?? "";
        var command = new ChatsBackend_ChangeEntry(
            ChatEntryId.New(chatId, 0),
            null,
            Change.Create(new ChatEntryDiff {
                Kind = ChatEntryKind.HistoryChanged,
                AuthorId = Bots.GetWalleId(chatId),
                TargetAuthorId = authorId,
                TargetAuthorName = authorName.NullIfEmpty() ?? MentionMarkup.NotAvailableName,
                HistoryChange = change,
                HistoryPeriod = period,
            }));
        await Commander.Call(command, true, cancellationToken).ConfigureAwait(false);
    }
}
