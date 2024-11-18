namespace ActualChat.Chat;

internal static class UserLinksBackendExt
{
    public static async Task UpdateUserLink(
        ICommander commander,
        UserLinkId oldUserLinkId,
        UserLinkId userLinkId,
        UserLinkKind kind,
        ISymbolIdentifier identifier,
        CancellationToken cancellationToken)
    {
        if (oldUserLinkId == userLinkId)
            return;

        if (!oldUserLinkId.IsNone) {
            var removeCommand = new UserLinksBackend_Change(oldUserLinkId, null, Change.Remove<UserLink>());
            await commander.Call(removeCommand, false, cancellationToken).ConfigureAwait(false);
        }

        if (!userLinkId.IsNone) {
            var createCommand = new UserLinksBackend_Change(userLinkId, null, Change.Create(new UserLink(userLinkId) {
                Kind = kind,
                TargetId = identifier.Value,
            }));
            await commander.Call(createCommand, false, cancellationToken).ConfigureAwait(false);
        }
    }
}
