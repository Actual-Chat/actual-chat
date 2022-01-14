using System.Text;
using ActualChat.Users.Db;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Users;

public class UserInfos : DbServiceBase<UsersDbContext>, IUserInfos
{
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

    public virtual async Task<string?> GetGravatarHash(string userId, CancellationToken cancellationToken)
    {
        var user = await _authBackend.GetUser(userId, cancellationToken).ConfigureAwait(false);
        var email = user?.Claims.GetValueOrDefault(System.Security.Claims.ClaimTypes.Email) ?? "";
        if (email.IsNullOrEmpty())
            return null;
        return CreateMD5(email.Trim().ToLowerInvariant());
    }

    private static string CreateMD5(string input)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        var inputBytes = Encoding.ASCII.GetBytes(input);
        var hashBytes = md5.ComputeHash(inputBytes);
        var sb = new StringBuilder();
        foreach (var @byte in hashBytes)
            sb.Append(@byte.ToString("x2"));
        return sb.ToString();
    }
}
