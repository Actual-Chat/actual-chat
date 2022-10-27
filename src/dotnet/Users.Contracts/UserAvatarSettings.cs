namespace ActualChat.Users;

[DataContract]
public sealed record UserAvatarSettings
{
    internal const string KvasKey = nameof(UserAvatarSettings);

    [DataMember] public ImmutableArray<Symbol> AvatarIds { get; init; } = ImmutableArray<Symbol>.Empty;
    [DataMember] public Symbol DefaultAvatarId { get; init; }

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
        return this with { AvatarIds = AvatarIds.RemoveAll(x => x == avatarId) };
    }
}
