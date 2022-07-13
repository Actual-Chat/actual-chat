using System.Security;

namespace ActualChat.Users;

public sealed record UserProfile(Symbol Id, User User) : IHasId<Symbol>, IRequirementTarget
{
    public static UserProfile Guest { get; } = new(Symbol.Empty, User.NewGuest()) { Status = UserStatus.Inactive };

    public static Requirement<UserProfile> MustBeActive { get; } = Requirement.New(
        new("User is either suspended or not activated yet.", m => new SecurityException(m)),
        (UserProfile? p) => p != null && (p.IsActive() || p.IsAdmin));
    public static Requirement<UserProfile> MustBeAdmin { get; } = Requirement.New(
        new("Only administrators can perform this action.", m => new SecurityException(m)),
        (UserProfile? p) => p?.IsAdmin ?? false);

    public long Version { get; init; }
    public UserStatus Status { get; init; }
    public Symbol AvatarId { get; init; }
    public bool IsAdmin { get; init; }
}
