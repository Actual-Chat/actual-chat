using System.Security.Claims;
using CommunityToolkit.HighPerformance;

namespace ActualChat.Users;

/// <summary>
/// Describes an account backed by <see cref="UserIdentity.InternalSchema"/> —
/// the admin, well-known bots, and generated test users.
/// </summary>
public sealed record InternalUserInfo(
    UserId Id,
    string UserName = "",
    string FirstName = "",
    string LastName = "",
    string Phone = "",
    string Email = "",
    string AvatarName = "")
{
    public string AvatarBio { get; init; } = "";
    public MediaId? AvatarMediaId { get; init; }
    public string AvatarPictureUrl { get; init; } = "";
    public bool HasGeneratedPicture { get; init; } = true;

    public string UserNameOrDefault
        => !UserName.IsNullOrEmpty() ? UserName : $"{FirstName.ToLower()}_{LastName.ToLower()}";
    public string AvatarNameOrDefault
        => AvatarName.NullIfEmpty() ?? $"{FirstName} {LastName}".Trim().NullIfEmpty() ?? UserNameOrDefault;
}

public static class InternalAccounts
{
    public static async Task<AccountFull> Create(
        IServiceProvider services,
        InternalUserInfo userInfo,
        CancellationToken cancellationToken)
    {
        var userId = userInfo.Id;
        var commander = services.Commander();
        var accountsBackend = services.GetRequiredService<IAccountsBackend>();
        var isAdmin = userId == Constants.User.Admin.UserId;

        var identities = new ApiMap<UserIdentity, string>();
        var claims = new ApiMap<string, string>();
        if (!userInfo.FirstName.IsNullOrEmpty())
            claims = claims.With(ClaimTypes.GivenName, userInfo.FirstName);
        if (!userInfo.LastName.IsNullOrEmpty())
            claims = claims.With(ClaimTypes.Surname, userInfo.LastName);
        if (!userInfo.Phone.IsNullOrEmpty()) {
            var phone = ActualChat.Phone.Parse(userInfo.Phone);
            identities = identities.WithPhoneIdentity(phone);
            claims = claims.With(ClaimTypes.MobilePhone, phone.Value);
        }
        if (!userInfo.Email.IsNullOrEmpty())
            claims = claims.With(ClaimTypes.Email, userInfo.Email);

        var userIdentity = new UserIdentity(UserIdentity.InternalSchema, userId.Value);
        var signInCommand = new AccountsBackend_SignIn(
            Session.New(), userIdentity, identities, claims, AutoCreate: true);
        await commander.Call(signInCommand, true, cancellationToken).ConfigureAwait(false);

        var account = await accountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);
        account.Require();
        if (account.Status != AccountStatus.Active) {
            account = account with { Status = AccountStatus.Active };
            var updateCommand = new AccountsBackend_Update(account, account.Version);
            await commander.Call(updateCommand, true, cancellationToken).ConfigureAwait(false);
            account = await accountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);
        }
        account.Require(isAdmin ? AccountFull.MustBeAdmin : AccountFull.MustBeActive);

        var avatarMediaId = userInfo.AvatarMediaId;
        var avatarPictureUrl = "";
        if (avatarMediaId == null && userInfo.HasGeneratedPicture)
            avatarPictureUrl = userInfo.AvatarPictureUrl.NullIfEmpty()
                ?? $"https://api.dicebear.com/7.x/bottts/svg?seed={userId.Value.GetDjb2HashCode()}";

        var changeAvatarCommand = new AvatarsBackend_Change(Symbol.Empty, null,
            Change.Create(new AvatarDiff {
                UserId = userId,
                Name = userInfo.AvatarNameOrDefault,
                Bio = userInfo.AvatarBio.NullIfEmpty() ?? $"I'm just a {userInfo.UserNameOrDefault} test bot",
                MediaId = Option.Some(avatarMediaId),
                PictureUrl = avatarPictureUrl,
            }));
        var avatar = await commander.Call(changeAvatarCommand, true, cancellationToken).ConfigureAwait(false);

        var serverKvasBackend = services.GetRequiredService<IServerKvasBackend>();
        var userKvas = serverKvasBackend.ForUser(account);
        var userAvatarSettings = new UserAvatarSettings() {
            DefaultAvatarId = avatar.Id,
            AvatarIds = [avatar.Id],
        };
        await userKvas.UserAvatarSettings().Set(userAvatarSettings, cancellationToken).ConfigureAwait(false);

        account = await accountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);
        account.Require(isAdmin ? AccountFull.MustBeAdmin : AccountFull.MustBeActive);
        if (account.Avatar.Require().Id != avatar.Id)
            throw StandardError.Internal("Wrong avatar ID.");

        return account;
    }
}
