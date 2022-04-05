using System.Net.Mail;
using ActualChat.Users.Db;
using Microsoft.AspNetCore.Authentication.Google;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Users;

public class UserInfos : DbServiceBase<UsersDbContext>, IUserInfos
{
    private const string AdminEmailDomain = "actual.chat";

    private readonly IAuthBackend _authBackend;
    private readonly IDbEntityResolver<string, DbUser> _dbUserResolver;
    private readonly DbUserByNameResolver _dbUserByNameResolver;

    public UserInfos(IServiceProvider services) : base(services)
    {
        _authBackend = services.GetRequiredService<IAuthBackend>();
        _dbUserResolver = services.DbEntityResolver<string, DbUser>();
        _dbUserByNameResolver = services.GetRequiredService<DbUserByNameResolver>();
    }

    public virtual async Task<UserInfo?> Get(string userId, CancellationToken cancellationToken)
    {
        var dbUser = await _dbUserResolver.Get(userId, cancellationToken).ConfigureAwait(false);
        return dbUser == null ? null : new UserInfo(dbUser.Id, dbUser.Name);
    }

    public virtual async Task<UserInfo?> GetByName(string name, CancellationToken cancellationToken)
    {
        var dbUser = await _dbUserByNameResolver.Get(name, cancellationToken).ConfigureAwait(false);
        return dbUser == null ? null : new UserInfo(dbUser.Id, dbUser.Name);
    }

    public virtual async Task<string> GetGravatarHash(string userId, CancellationToken cancellationToken)
    {
        var user = await _authBackend.GetUser(userId, cancellationToken).ConfigureAwait(false);
        var email = user?.Claims.GetValueOrDefault(System.Security.Claims.ClaimTypes.Email) ?? "";
        return email.IsNullOrEmpty() ? "" : email.Trim().ToLowerInvariant().GetMD5HashCode();
    }

    public virtual async Task<bool> IsAdmin(string userId, CancellationToken cancellationToken)
    {
        var user = await _authBackend.GetUser(userId, cancellationToken).ConfigureAwait(false);
        if (user == null || !user.IsAuthenticated)
            return false;

        // TODO: looks odd when we hardcode some tests related auth providers in production code.
        if (HasIdentity(user, "internal") || HasIdentity(user, "test"))
            return true;

        return BelongsToActualChatDomain(user);
    }

    private bool BelongsToActualChatDomain(User user)
    {
        if (!HasIdentity(user, GoogleDefaults.AuthenticationScheme))
            return false;

        var email = user.Claims.GetValueOrDefault(System.Security.Claims.ClaimTypes.Email);
        if (string.IsNullOrEmpty(email) || !MailAddress.TryCreate(email, out var mailAddress))
            return false;

        return mailAddress.Host.Equals(AdminEmailDomain, StringComparison.OrdinalIgnoreCase);
    }

    private bool HasIdentity(User user, string provider)
        => user.Identities.Keys.Select(x => x.Schema).Contains(provider, StringComparer.OrdinalIgnoreCase);
}
