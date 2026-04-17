using ActualLab.Rpc;

namespace ActualChat.Users;

/// <summary>
/// Legacy IAvatars service for backward compatibility with old mobile clients
/// that send Avatars_FullChange (Change&lt;AvatarFull&gt;) instead of Avatars_Change (Change&lt;AvatarDiff&gt;).
/// Remove once all clients are migrated.
/// </summary>
[LegacyName("IAvatars", "2.7.9999")]
[Obsolete("Legacy compat for old mobile clients using Change<AvatarFull>")]
public interface ILegacyAvatars : IComputeService
{
    [CommandHandler]
    [LegacyName("OnChange", "2.7.9999")]
    Task<AvatarFull> OnLegacyChange(Avatars_FullChange command, CancellationToken cancellationToken);
}
