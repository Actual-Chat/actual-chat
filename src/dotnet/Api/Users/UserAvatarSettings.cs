using MemoryPack;

namespace ActualChat.Users;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record UserAvatarSettings
{
    public const string KvasKey = nameof(UserAvatarSettings);

    [DataMember, MemoryPackOrder(0), MemoryPackInclude]
    private ApiArray<Symbol> LegacyAvatarIds {
        get => AvatarIds.ToApiArray();
        init => AvatarIds = value.ToList();
    }

    [IgnoreDataMember, MemoryPackIgnore]
    public IReadOnlyList<Symbol> AvatarIds { get; init; } = [];

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
