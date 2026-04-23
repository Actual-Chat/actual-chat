using ActualLab.Rpc;

namespace ActualChat.Users;

/// <summary>
/// Legacy IAvatars service for backward compatibility with old mobile clients
/// that send Avatars_FullChange (Change&lt;AvatarFull&gt;) instead of Avatars_Change (Change&lt;AvatarDiff&gt;).
/// Remove once all clients are migrated.
/// </summary>
[LegacyName("IAvatars", "2.7.9999")]
[Obsolete("2026.04: Legacy compat for old mobile clients using Change<AvatarFull>")]
public interface ILegacyAvatars : IComputeService
{
    [CommandHandler]
    [LegacyName("OnChange", "2.7.9999")]
    Task<AvatarFull> OnLegacyChange(Avatars_FullChange command, CancellationToken cancellationToken);
}

/// <summary>
/// Legacy command using full AvatarFull for backward compatibility with old mobile clients.
/// Same wire layout as the pre-diff Avatars_Change.
/// </summary>
[Obsolete("2026.04: Legacy compat for old mobile clients using Change<AvatarFull>")]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record Avatars_FullChange(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), Key(1)] Symbol AvatarId,
    [property: DataMember, MemoryPackOrder(2), Key(2)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(3), Key(3)] Change<AvatarFull> Change
) : ISessionCommand<AvatarFull>, IApiCommand;
