using System.Text;
using ActualChat.Users.Db;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Users;

public class UserAuthorsBackend : DbServiceBase<UsersDbContext>, IUserAuthorsBackend
{
    private readonly IUserInfos _userInfos;
    private readonly IDbEntityResolver<string, DbUserAuthor> _dbUserAuthorResolver;

    public UserAuthorsBackend(IServiceProvider services) : base(services)
    {
        _userInfos = Services.GetRequiredService<IUserInfos>();
        _dbUserAuthorResolver = Services.GetRequiredService<IDbEntityResolver<string, DbUserAuthor>>();
    }

    // [ComputeMethod]
    public virtual async Task<UserAuthor?> Get(string userId, bool inherit, CancellationToken cancellationToken)
    {
        if (userId.IsNullOrEmpty())
            return null;

        var dbUserAuthor = await _dbUserAuthorResolver.Get(userId, cancellationToken).ConfigureAwait(false);
        var userAuthor = dbUserAuthor?.ToModel();
        if (!inherit || userAuthor == null)
            return userAuthor;

        var userInfo = await _userInfos.Get(userId, cancellationToken).ConfigureAwait(false);
        var result = userAuthor.InheritFrom(userInfo);
        if (result.Picture.IsNullOrEmpty()) {
            var gravatarHash = await _userInfos.GetGravatarHash(userId, cancellationToken).ConfigureAwait(false);
            if (!gravatarHash.IsNullOrEmpty()) {
                var gravatarPic = "https://www.gravatar.com/avatar/" + gravatarHash + "?d=identicon";
                result = result with { Picture = gravatarPic };
            }
        }
        return result;
    }
}
