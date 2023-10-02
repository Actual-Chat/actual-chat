using MemoryPack;

namespace ActualChat.Users;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record UserAvatarSettings
{
    public const string KvasKey = nameof(UserAvatarSettings);

    [DataMember, MemoryPackOrder(0)] public ApiArray<Symbol> AvatarIds { get; init; }
    [DataMember, MemoryPackOrder(1)] public Symbol DefaultAvatarId { get; init; }

    public UserAvatarSettings WithAvatarId(Symbol avatarId)
    {
        if (AvatarIds.Contains(avatarId))
            return this;

        return this with { AvatarIds = AvatarIds.Add(avatarId) };
    }

    public UserAvatarSettings WithoutAvatarId(Symbol avatarId)
    {
        if (!AvatarIds.Contains(avatarId))
            return this;

        var avatars = AvatarIds.RemoveAll(x => x == avatarId);
        var defaultAvatarId = DefaultAvatarId != avatarId ? DefaultAvatarId : avatars.FirstOrDefault();
        return this with { AvatarIds = avatars, DefaultAvatarId = defaultAvatarId };
    }
}
