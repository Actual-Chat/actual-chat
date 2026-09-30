using ActualChat.Users.Db;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Users;

/// <summary>
/// Frontend service for managing user's read positions in chats.
/// </summary>
[SuppressMessage("Usage", "MA0006:Use String.Equals instead of equality operator")]
public class ChatPositions(IServiceProvider services) : DbServiceBase<UsersDbContext>(services), IChatPositions
{
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IChats Chats { get; } = services.GetRequiredService<IChats>();
    private IChatPositionsBackend Backend { get; } = services.GetRequiredService<IChatPositionsBackend>();

    // [ComputeMethod]
    public virtual async Task<ChatPosition> GetOwn(
        Session session, ChatId chatId, ChatPositionKind kind,
        CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        return await Backend.Get(account.Id, chatId, kind, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnSet(ChatPositions_Set command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var session = command.Session;
        var chatId = command.ChatId;
        var kind = command.Kind;
        var position = command.Position;
        if (kind == ChatPositionKind.Heard)
            throw StandardError.Constraint(
                "Heard position can't be set directly - it's owned by the server-side ack path.");

        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat is null)
            return;

        if (kind == ChatPositionKind.Read) {
            // Read positions are stored forward-only, so one past the chat's end permanently marks
            // entries that don't exist yet as read. Clients overshoot in more than one way: the
            // "unbounded" long.MaxValue sentinel, and the synthetic lids the chat view gives to
            // placeholder items - an optimistic send, the audio-recording skeleton.
            // View positions are excluded: they're overwritten freely and mean "where you were".
            var idRange = await Chats.GetIdRange(session, chatId, cancellationToken).ConfigureAwait(false);
            var maxLid = idRange.End - 1;
            if (position.EntryLid > maxLid)
                position = position with { EntryLid = maxLid };
        }

        var backendCommand = new ChatPositionsBackend_Set(account.Id, chatId, kind, position);
        await Commander.Call(backendCommand, true, cancellationToken).ConfigureAwait(false);
    }
}
