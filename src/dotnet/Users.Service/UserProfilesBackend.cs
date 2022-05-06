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

    private IUserAuthorsBackend? _userAuthorsBackend; // Dep. cycle elimination
    private readonly IDbEntityConverter<DbUserProfile, UserProfile> _converter;
    private readonly UsersSettings _usersSettings;

    private IAuthBackend AuthBackend { get; }

    private IUserAuthorsBackend UserAuthorsBackend
        => _userAuthorsBackend ??= Services.GetRequiredService<IUserAuthorsBackend>();
    private DbUserByNameResolver DbUserByNameResolver { get; }

    public UserProfilesBackend(IServiceProvider services, IDbEntityConverter<DbUserProfile, UserProfile> converter, UsersSettings usersSettings) : base(services)
    {
        _converter = converter;
        _usersSettings = usersSettings;
        AuthBackend = services.GetRequiredService<IAuthBackend>();
        DbUserByNameResolver = services.GetRequiredService<DbUserByNameResolver>();
    }

    // [ComputeMethod]
    public virtual async Task<UserProfile?> Get(string id, CancellationToken cancellationToken)
    {
        var user = await AuthBackend.GetUser(id, cancellationToken).ConfigureAwait(false);

        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);

        var dbUserProfile = await GetDbUserProfile(dbContext, id, cancellationToken).ConfigureAwait(false);
        return await ToUserProfile(dbUserProfile, user, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<UserProfile?> GetByName(string name, CancellationToken cancellationToken)
    {
        var dbUser = await DbUserByNameResolver.Get(name, cancellationToken).ConfigureAwait(false);
        if (dbUser == null) return null;

        return await Get(dbUser.Id, cancellationToken).ConfigureAwait(false);
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

        var user = await AuthBackend.GetUser(command.UserProfileOrUserId, cancellationToken).ConfigureAwait(false);
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
        _converter.UpdateEntity(userProfile, dbUserProfile);
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

    private async Task<UserProfile?> ToUserProfile(
        DbUserProfile dbUserProfile,
        User? user,
        CancellationToken cancellationToken)
    {
        if (user == null || !user.IsAuthenticated)
            return null;

        var userAuthor = await UserAuthorsBackend.Get(user.Id, false, cancellationToken).ConfigureAwait(false);
        var userProfile = _converter.ToModel(dbUserProfile) with {
            User = user,
            Picture = userAuthor?.Picture.NullIfEmpty() ?? GetDefaultPicture(user),
            IsAdmin = IsAdmin(user),
        };
        return userProfile;
    }

    private string GetDefaultPicture(User? user, int size = 80)
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
