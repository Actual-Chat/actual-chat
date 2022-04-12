using System.Net.Mail;
using ActualChat.Users.Db;
using Microsoft.AspNetCore.Authentication.Google;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Users;

public class UserProfilesBackend : DbServiceBase<UsersDbContext>, IUserProfilesBackend
{
    private const string AdminEmailDomain = "actual.chat";

    private IUserAuthorsBackend? _userAuthorsBackend; // Dep. cycle elimination

    private IAuthBackend AuthBackend { get; }
    private IUserAuthorsBackend UserAuthorsBackend
        => _userAuthorsBackend ??= Services.GetRequiredService<IUserAuthorsBackend>();
    private DbUserByNameResolver DbUserByNameResolver { get; }

    public UserProfilesBackend(IServiceProvider services) : base(services)
    {
        AuthBackend = services.GetRequiredService<IAuthBackend>();
        DbUserByNameResolver = services.GetRequiredService<DbUserByNameResolver>();
    }

    // [ComputeMethod]
    public virtual async Task<UserProfile?> Get(string userId, CancellationToken cancellationToken)
    {
        var user = await AuthBackend.GetUser(userId, cancellationToken).ConfigureAwait(false);
        return await ToUserProfile(user, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<UserProfile?> GetByName(string name, CancellationToken cancellationToken)
    {
        var dbUser = await DbUserByNameResolver.Get(name, cancellationToken).ConfigureAwait(false);
        if (dbUser == null) return null;

        var user = await AuthBackend.GetUser(dbUser.Id, cancellationToken).ConfigureAwait(false);
        return await ToUserProfile(user, cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private async Task<UserProfile?> ToUserProfile(User? user, CancellationToken cancellationToken)
    {
        if (user == null || !user.IsAuthenticated)
            return null;

        var userAuthor = await UserAuthorsBackend.Get(user.Id, false, cancellationToken).ConfigureAwait(false);
        var userProfile = new UserProfile(user.Id, user.Name, user) {
            Picture = userAuthor?.Picture.NullIfEmpty() ?? GetDefaultPicture(user),
            IsAdmin = IsAdmin(user),
        };
        return userProfile;
    }

    private string GetDefaultPicture(User? user, int size = 480)
    {
        if (user == null) return "";

        var email = (user.Claims.GetValueOrDefault(System.Security.Claims.ClaimTypes.Email) ?? "")
            .Trim().ToLowerInvariant();
        if (!email.IsNullOrEmpty()) {
            var emailHash = email.GetMD5HashCode().ToLowerInvariant();
            return $"https://www.gravatar.com/avatar/{emailHash}?s={size}";
        }
        var name = user.Name.NullIfEmpty() ?? "@" +user.Id;
        var nameHash = name.GetMD5HashCode().ToLowerInvariant();
        return $"https://avatars.dicebear.com/api/avataaars/{nameHash}.svg";
    }

    private bool IsAdmin(User user)
    {
        if (!user.IsAuthenticated) return false;

        // TODO(AY): Remove the check relying on test/internal auth providers in the production code
        if (HasIdentity(user, "internal") || HasIdentity(user, "test"))
            return true;

        return IsActualChatDomainUser(user);
    }

    private bool IsActualChatDomainUser(User user)
    {
        if (!HasIdentity(user, GoogleDefaults.AuthenticationScheme))
            return false;

        var email = user.Claims.GetValueOrDefault(System.Security.Claims.ClaimTypes.Email);
        if (email.IsNullOrEmpty() || !MailAddress.TryCreate(email, out var mailAddress))
            return false;

        return StringComparer.Ordinal.Equals(mailAddress.Host, AdminEmailDomain);
    }

    private bool HasIdentity(User user, string provider)
        => user.Identities.Keys.Select(x => x.Schema).Contains(provider, StringComparer.Ordinal);
}
