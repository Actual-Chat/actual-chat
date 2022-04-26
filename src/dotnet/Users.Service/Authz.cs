namespace ActualChat.Users;

internal class Authz : IAuthz
{
    private readonly IUserProfiles _userProfiles;

    public Authz(IUserProfiles userProfiles)
        => _userProfiles = userProfiles;

    // [ComputeMethod]
    public virtual async Task<bool> IsActive(Session session, CancellationToken cancellationToken)
    {
        var userProfile = await _userProfiles.Get(session, cancellationToken).ConfigureAwait(false);
        return userProfile?.Status == UserStatus.Active;
    }
}
