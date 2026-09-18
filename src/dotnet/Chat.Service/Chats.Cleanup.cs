namespace ActualChat.Chat;

public partial class Chats
{
    public virtual async Task<long> OnCleanup(Chats_Cleanup command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return default;

        var chat = await Get(command.Session, command.ChatId, cancellationToken).Require().ConfigureAwait(false);
        chat.Rules.Permissions.Require(ChatPermissions.Owner);
        ThrowIfPlaceRootChat(chat.Id);
        await Maintenances.RequireAvailable(chat.Id, cancellationToken).ConfigureAwait(false);
        return await Commander.Call(
            new ChatsBackend_AdvanceVisibilityBoundary(chat.Id, command.MinVisibleEntryLid, command.ExpectedVersion),
            true, cancellationToken).ConfigureAwait(false);
    }
}
