namespace ActualChat.Chat;

internal static class AliasBackendExt
{
    public static async Task UpdateAlias(
        this ICommander commander,
        AliasId? oldAliasId,
        AliasId? aliasId,
        AliasKind kind,
        string targetId,
        CancellationToken cancellationToken)
    {
        if (oldAliasId == aliasId)
            return;

        if (oldAliasId is not null) {
            var removeCommand = new AliasBackend_Change(oldAliasId, null, Change.Remove<Alias>());
            await commander.Call(removeCommand, false, cancellationToken).ConfigureAwait(false);
        }

        if (aliasId is not null) {
            var createCommand = new AliasBackend_Change(aliasId, null, Change.Create(new Alias(aliasId) {
                Kind = kind,
                TargetId = targetId,
            }));
            await commander.Call(createCommand, false, cancellationToken).ConfigureAwait(false);
        }
    }
}
