using System.Net.Mail;
using ActualChat.Users.Db;
using ActualChat.Users.Module;
using Microsoft.AspNetCore.Authentication.Google;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Users;

public class UserProfilesBackend : DbServiceBase<UsersDbContext>, IUserProfilesBackend
{
    private const string AdminEmailDomain = "actual.chat";
    private HashSet<string> AdminEmails { get; } = new(StringComparer.Ordinal) { "alex.yakunin@gmail.com" };

    private readonly UsersSettings _usersSettings;
    private readonly DbUserRepo _dbUserRepo;
    private readonly IDbEntityConverter<DbUser, User> _userConverter;
    private readonly IUserAvatarsBackend _userAvatarsBackend;

    private IAuthBackend AuthBackend { get; }

    private DbUserByNameResolver DbUserByNameResolver { get; }

    public UserProfilesBackend(IServiceProvider services, UsersSettings usersSettings) : base(services)
    {
        _usersSettings = usersSettings;
        _dbUserRepo = services.GetRequiredService<DbUserRepo>();
        _userConverter = services.GetRequiredService<IDbEntityConverter<DbUser, User>>();
        _userAvatarsBackend = services.GetRequiredService<IUserAvatarsBackend>();
        AuthBackend = services.GetRequiredService<IAuthBackend>();
        DbUserByNameResolver = services.GetRequiredService<DbUserByNameResolver>();
    }

    // [ComputeMethod]
    public virtual async Task<UserProfile?> Get(string id, CancellationToken cancellationToken)
    {
        var user = await AuthBackend.GetUser(id, cancellationToken).ConfigureAwait(false);
        if (user == null || !user.IsAuthenticated)
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

        var user = await AuthBackend.GetUser(userId, cancellationToken).ConfigureAwait(false);
        if (user == null)
            return null;

        var userAuthor = new UserAuthor { Name = user.Name };

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
    public virtual async Task Create(IUserProfilesBackend.CreateCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating()) {
            _ = Get(command.UserProfileOrUserId, default);
            return;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbUserProfile = await dbContext.UserProfiles
            .FindAsync(DbKey.Compose(command.UserProfileOrUserId), cancellationToken)
            .ConfigureAwait(false);
        if (dbUserProfile != null)
            return;

        var dbUser = await _dbUserRepo.Get(dbContext, command.UserProfileOrUserId, false, cancellationToken).ConfigureAwait(false);
        var user = _userConverter.ToModel(dbUser);
        var isAdmin = user != null && IsAdmin(user);

        dbUserProfile = new DbUserProfile {
            Id = command.UserProfileOrUserId,
            Status = isAdmin ? UserStatus.Active : _usersSettings.NewUserStatus,
            Version = VersionGenerator.NextVersion(),
        };

        await dbContext.AddAsync(dbUserProfile, cancellationToken).ConfigureAwait(false);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
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

    private async Task<DbUserProfile> GetDbUserProfile(
        UsersDbContext context,
        string userId,
        CancellationToken cancellationToken)
        => await context.UserProfiles
                .FindAsync(DbKey.Compose(userId), cancellationToken)
                .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"User profile id='{userId}' not found");

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

    private bool IsAdmin(User user)
    {
        if (!user.IsAuthenticated) return false;

        // TODO(AY): Remove the check relying on test/internal auth providers in the production code
        if (HasIdentity(user, "internal") || HasIdentity(user, "test"))
            return true;

        var email = user.Claims.GetValueOrDefault(System.Security.Claims.ClaimTypes.Email);
        if (email.IsNullOrEmpty() || !MailAddress.TryCreate(email, out var emailAddress))
            return false;

        if (AdminEmails.Contains(email))
            return true; // Predefined admin email
        if (HasGoogleIdentity(user) && StringComparer.Ordinal.Equals(emailAddress.Host, AdminEmailDomain))
            return true; // actual.chat email
        return false;
    }

    private bool HasGoogleIdentity(User user)
        => HasIdentity(user, GoogleDefaults.AuthenticationScheme);

    private bool HasIdentity(User user, string provider)
        => user.Identities.Keys.Select(x => x.Schema).Contains(provider, StringComparer.Ordinal);
}
