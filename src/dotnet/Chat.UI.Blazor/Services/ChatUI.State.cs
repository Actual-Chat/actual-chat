using ActualChat.Pooling;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.Services;

public partial class ChatUI
{
    private readonly SharedResourcePool<Symbol, IPersistentState<long>> _lastReadEntryIds;

    public async Task<IPersistentState<long>> GetLastReadEntryId(Symbol chatId, CancellationToken cancellationToken)
    {
        var lease = await _lastReadEntryIds.Rent(chatId, cancellationToken).ConfigureAwait(false);
        return new PersistentStateReplica<long>(lease);
    }

    private Task<IPersistentState<long>> RestoreLastReadEntryId(Symbol chatId, CancellationToken cancellationToken)
        => StateFactory.NewPersistent(
            ct => ChatReadPositions.GetReadPosition(Session, chatId, ct),
            lastReadEntryId => new IChatReadPositions.UpdateReadPositionCommand(Session, chatId, lastReadEntryId),
            cancellationToken);
}
