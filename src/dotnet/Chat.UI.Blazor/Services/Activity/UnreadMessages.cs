using ActualChat.Kvas;
using Stl.Locking;

namespace ActualChat.Chat.UI.Blazor.Services;

public class UnreadMessages : IDisposable
{
    private readonly Session _session;
    private readonly IChats _chats;
    private readonly Symbol _chatId;
    private readonly ChatUI _chatUI;
    private readonly AsyncLock _asyncLock = new(ReentryMode.CheckedFail);
    private SyncedStateLease<long>? _lastReadEntryState;

    public UnreadMessages(Session session, Symbol chatId, ChatUI chatUI, IChats chats)
    {
        _session = session;
        _chats = chats;
        _chatId = chatId;
        _chatUI = chatUI;
    }

    public void Dispose()
        => _lastReadEntryState?.Dispose();

    public async Task<int?> GetCount(CancellationToken cancellationToken)
    {
        using (var _ = await _asyncLock.Lock(cancellationToken).ConfigureAwait(false))
            _lastReadEntryState ??= await _chatUI.LeaseLastReadEntryState(_chatId, cancellationToken).ConfigureAwait(false);

        var lastReadEntryId = await _lastReadEntryState.Use(cancellationToken).ConfigureAwait(false);
        if (lastReadEntryId == 0)
            return null;

        var lastTile1 = await _chats.GetLastIdTile1(_session, _chatId, ChatEntryType.Text, cancellationToken)
            .ConfigureAwait(false);
        var unreadCount = Math.Max((int)(lastTile1.Start - lastReadEntryId), 0);
        switch (unreadCount) {
        case > 100:
        {
            using var _ = Computed.SuspendDependencyCapture();
            var idRange = await _chats.GetIdRange(_session, _chatId, ChatEntryType.Text, cancellationToken)
                .ConfigureAwait(false);
            unreadCount = Math.Max((int)(idRange.ToInclusive().End - lastReadEntryId), 0);
            return unreadCount;
        }
        case > 10:
        {
            var __ = await _chats.GetLastIdTile0(_session, _chatId, ChatEntryType.Text, cancellationToken).ConfigureAwait(false);
            using var _ = Computed.SuspendDependencyCapture();
            var idRange = await _chats.GetIdRange(_session, _chatId, ChatEntryType.Text, cancellationToken)
                .ConfigureAwait(false);
            unreadCount = Math.Max((int)(idRange.ToInclusive().End - lastReadEntryId), 0);
            return unreadCount;
        }
        default:
        {
            var idRange = await _chats.GetIdRange(_session, _chatId, ChatEntryType.Text, cancellationToken)
                .ConfigureAwait(false);
            unreadCount = Math.Max((int)(idRange.ToInclusive().End - lastReadEntryId), 0);
            return unreadCount;
        }}
    }
}
