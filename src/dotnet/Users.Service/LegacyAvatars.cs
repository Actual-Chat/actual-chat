namespace ActualChat.Users;

/// <summary>
/// Legacy IAvatars implementation for backward compatibility with old mobile clients
/// that send Avatars_FullChange (Change&lt;AvatarFull&gt;).
/// Converts to diff-based Avatars_Change and delegates to the real IAvatars service.
/// Remove once all clients are migrated.
/// </summary>
#pragma warning disable CS0618 // Obsolete
public class LegacyAvatars(IServiceProvider services) : ILegacyAvatars
{
    private ICommander Commander { get; } = services.Commander();

    public virtual async Task<AvatarFull> OnLegacyChange(
        Avatars_FullChange command, CancellationToken cancellationToken)
    {
        var (session, avatarId, expectedVersion, change) = command;

        Change<AvatarDiff> newChange;
        if (change.IsCreate(out var avatar))
            newChange = Change.Create(AvatarDiff.FromFull(avatar));
        else if (change.IsUpdate(out avatar))
            newChange = Change.Update(AvatarDiff.FromFull(avatar));
        else
            newChange = Change.Remove<AvatarDiff>();

        var newCommand = new Avatars_Change(session, avatarId, expectedVersion, newChange);
        return await Commander.Call(newCommand, true, cancellationToken).ConfigureAwait(false);
    }
}
#pragma warning restore CS0618
