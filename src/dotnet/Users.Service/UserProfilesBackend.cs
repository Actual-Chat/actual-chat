using System.Net.Mail;
using ActualChat.Users.Db;
using Microsoft.AspNetCore.Authentication.Google;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Users;

public class UserProfilesBackend : DbServiceBase<UsersDbContext>, IUserProfilesBackend
{
    private const string AdminEmailDomain = "actual.chat";
    private static HashSet<string> AdminEmails { get; } = new(StringComparer.Ordinal) { "alex.yakunin@gmail.com" };

    private readonly IUserAvatarsBackend _userAvatarsBackend;

    private IAuthBackend AuthBackend { get; }

    public UserProfilesBackend(IServiceProvider services) : base(services)
    {
        _userAvatarsBackend = services.GetRequiredService<IUserAvatarsBackend>();
        AuthBackend = services.GetRequiredService<IAuthBackend>();
    }

    // [ComputeMethod]
    public virtual async Task<UserProfile?> Get(string id, CancellationToken cancellationToken)
    {
        var user = await AuthBackend.GetUser(default, id, cancellationToken).ConfigureAwait(false);
        if (user == null)
            return null;

        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);

        var dbUserProfile = await GetDbUserProfile(dbContext, id, cancellationToken).ConfigureAwait(false);

        var userProfile =  new UserProfile(user.Id, user) {
            IsAdmin = IsAdmin(user),
        };
        userProfile = dbUserProfile.ToModel(userProfile);
        return userProfile;
    }

    public virtual async Task<UserAuthor?> GetUserAuthor(string userId, CancellationToken cancellationToken)
    {
        if (userId.IsNullOrEmpty())
            return null;

        var user = await AuthBackend.GetUser(default, userId, cancellationToken).ConfigureAwait(false);
        if (user == null)
            return null;

        var userAuthor = new UserAuthor { Id = user.Id, Name = user.Name };

        var profile = await Get(userId, cancellationToken).ConfigureAwait(false);
        if (profile == null)
            return userAuthor;

        if (!profile.AvatarId.IsEmpty) {
            var avatar = await _userAvatarsBackend.Get(profile.AvatarId, cancellationToken).ConfigureAwait(false);
            if (avatar != null) {
                userAuthor = userAuthor with { Picture = avatar.Picture };
            }
        }
        if (userAuthor.Picture.IsNullOrEmpty())
            userAuthor = userAuthor with { Picture = GetDefaultPicture(user) };
        return userAuthor;
    }

    // [CommandHandler]
    public virtual async Task Update(
        IUserProfilesBackend.UpdateCommand command,
        CancellationToken cancellationToken)
    {
        var userProfile = command.UserProfile;
        if (Computed.IsInvalidating()) {
            _ = Get(userProfile.Id, default);
            return;
        }

        var context = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = context.ConfigureAwait(false);

        var dbUserProfile = await GetDbUserProfile(context, userProfile.Id, cancellationToken).ConfigureAwait(false);
        dbUserProfile.UpdateFrom(userProfile);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private ValueTask<DbUserProfile> GetDbUserProfile(
        UsersDbContext context,
        string userId,
        CancellationToken cancellationToken)
        => context.UserProfiles.FindAsync(DbKey.Compose(userId), cancellationToken).Required();

    private string GetDefaultPicture(User? user, int size = 80)
    {
        if (user == null) return "";

        var email = (user.Claims.GetValueOrDefault(System.Security.Claims.ClaimTypes.Email) ?? "")
            .Trim().ToLowerInvariant();
        if (!email.IsNullOrEmpty()) {
            var emailHash = email.GetMD5HashCode().ToLowerInvariant();
            return $"https://www.gravatar.com/avatar/{emailHash}?s={size}";
        }
        return "";
    }

    internal static bool IsAdmin(User user)
    {
        // TODO(AY): Remove the check relying on test/internal auth providers in the production code
        if (HasIdentity(user, "internal") || HasIdentity(user, "test"))
            return true;

        var email = user.Claims.GetValueOrDefault(System.Security.Claims.ClaimTypes.Email);
        if (email.IsNullOrEmpty() || !MailAddress.TryCreate(email, out var emailAddress))
            return false;

        if (AdminEmails.Contains(email))
            return true; // Predefined admin email
        if (HasGoogleIdentity(user) && OrdinalEquals(emailAddress.Host, AdminEmailDomain))
            return true; // actual.chat email
        return false;
    }

    private static bool HasGoogleIdentity(User user)
        => HasIdentity(user, GoogleDefaults.AuthenticationScheme);

    private static bool HasIdentity(User user, string provider)
        => user.Identities.Keys.Select(x => x.Schema).Contains(provider, StringComparer.Ordinal);
}
