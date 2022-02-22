using System.Text;
using ActualChat.Users.Db;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Users;

public class UserInfos : DbServiceBase<UsersDbContext>, IUserInfos
{
    private static readonly HashSet<string> AdminEmails = new(StringComparer.Ordinal) {
        "alex.yakunin@actual.chat",
        "alex.yakunin@gmail.com",
        "alexey.kochetov@actual.chat",
        "undead00@gmail.com",
        "vladimir.chirikov@actual.chat",
        "vovanchig@gmail.com",
        "dmitry.filippov@actual.chat",
        "crui3er@gmail.com",
        "andrey.yakunin@actual.chat",
        "iqmulator@gmail.com",
        "alexis.kochetov@gmail.com",
        "alexey.kochetov@actual.chat",
        "vobewaf244@douwx.com", // Test account
    };

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
        if (user.Identities.Any(i =>
                StringComparer.Ordinal.Equals(i.Key.Schema, "internal")
                || StringComparer.Ordinal.Equals(i.Key.Schema, "test")))
            return true;
        var email = user.Claims.GetValueOrDefault(System.Security.Claims.ClaimTypes.Email) ?? "";
        return AdminEmails.Contains(email);
    }
}
