using ActualChat.Kvas;
using Stl.Locking;

namespace ActualChat.Chat.UI.Blazor.Services;

public class UnreadMessages : IDisposable
{
    private const int MaxCount = 1000;

    private readonly Session _session;
    private readonly IChats _chats;
    private readonly Symbol _chatId;
    private readonly ChatUI _chatUI;
    private readonly AsyncLock _asyncLock = new(ReentryMode.CheckedFail);
    private SyncedStateLease<long?>? _lastReadEntryState;

    public UnreadMessages(Session session, Symbol chatId, ChatUI chatUI, IChats chats)
    {
        _session = session;
        _chats = chats;
        _chatId = chatId;
        _chatUI = chatUI;
    }

    public void Dispose()
        => _lastReadEntryState?.Dispose();

    public async Task<int> GetCount(CancellationToken cancellationToken)
    {
        using (var _ = await _asyncLock.Lock(cancellationToken).ConfigureAwait(false))
            _lastReadEntryState ??= await _chatUI.LeaseLastReadEntryState(_chatId, cancellationToken).ConfigureAwait(false);

        var lastReadEntryId = await _lastReadEntryState.Use(cancellationToken).ConfigureAwait(false) ?? 0;
        if (lastReadEntryId == 0)
            return 0; // Never opened this chat, so no unread messages

        var tile1 = await _chats
            .GetLastIdTile1(_session, _chatId, ChatEntryType.Text, cancellationToken)
            .ConfigureAwait(false);
        var estimatedCount = RoundTo(tile1.Start - lastReadEntryId, 100);
        if (estimatedCount >= 200)
            return estimatedCount;

        var tile0 = await _chats
            .GetLastIdTile0(_session, _chatId, ChatEntryType.Text, cancellationToken)
            .ConfigureAwait(false);
        estimatedCount = RoundTo(tile0.Start - lastReadEntryId, 10);
        if (estimatedCount >= 20)
            return estimatedCount;

        var idRange = await _chats
            .GetIdRange(_session, _chatId, ChatEntryType.Text, cancellationToken)
            .ConfigureAwait(false);
        var exactCount = (int)(idRange.End - lastReadEntryId - 1).Clamp(0, MaxCount);
        return exactCount;
    }

    private static int RoundTo(long value, long @base)
        => (int)(value / @base * @base).Clamp(0, MaxCount);
}
