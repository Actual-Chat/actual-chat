using ActualChat.Kvas;

namespace ActualChat.Users;

/// <summary>
/// User preferences for avatar management and defaults.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public sealed partial record UserAvatarSettings : StoredSettings, IHasKvasKey<UserAvatarSettings>
{
    [DataMember, MemoryPackOrder(0)] public ApiArray<Symbol> AvatarIds { get; init; } = [];
    [DataMember, MemoryPackOrder(1)] public Symbol DefaultAvatarId { get; init; }

    public UserAvatarSettings WithAvatarId(Symbol avatarId)
    {
        if (AvatarIds.Contains(avatarId))
            return this;

        return this with { AvatarIds = AvatarIds.With(avatarId) };
    }

    public UserAvatarSettings WithoutAvatarId(Symbol avatarId)
    {
        if (!AvatarIds.Contains(avatarId))
            return this;

        var avatars = AvatarIds.Without(x => x == avatarId);
        var defaultAvatarId = DefaultAvatarId != avatarId ? DefaultAvatarId : avatars.GetOrDefault(0);
        return new UserAvatarSettings {
            AvatarIds = avatars,
            DefaultAvatarId = defaultAvatarId
        };
    }
}
