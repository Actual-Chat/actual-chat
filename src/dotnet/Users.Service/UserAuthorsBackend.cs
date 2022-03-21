using System.Text;
using ActualChat.Users.Db;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Users;

public class UserAuthorsBackend : DbServiceBase<UsersDbContext>, IUserAuthorsBackend
{
    private readonly IUserInfos _userInfos;
    private readonly IDbEntityResolver<string, DbUserAuthor> _dbUserAuthorResolver;
    private readonly IUserAvatarsBackend _userAvatarsBackend;

    public UserAuthorsBackend(IServiceProvider services) : base(services)
    {
        _userInfos = Services.GetRequiredService<IUserInfos>();
        _dbUserAuthorResolver = Services.GetRequiredService<IDbEntityResolver<string, DbUserAuthor>>();
        _userAvatarsBackend = Services.GetRequiredService<IUserAvatarsBackend>();
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

        if (!userAuthor.AvatarId.IsEmpty) {
            var avatar = await _userAvatarsBackend.Get(userAuthor.AvatarId, cancellationToken).ConfigureAwait(false);
            var result = userAuthor;
            if (avatar != null) {
                result = result with {Name = avatar.Name, Picture = avatar.Picture};
            }
            return result;
        }
        else {
            var userInfo = await _userInfos.Get(userId, cancellationToken).ConfigureAwait(false);
            var result = userAuthor.InheritFrom(userInfo);
            if (result.Picture.IsNullOrEmpty()) {
                var gravatarHash = await _userInfos.GetGravatarHash(userId, cancellationToken).ConfigureAwait(false);
                if (!gravatarHash.IsNullOrEmpty()) {
                    var gravatarUrl = $"https://www.gravatar.com/avatar/{gravatarHash}?d=identicon";
                    result = result with { Picture = gravatarUrl };
                }
            }
            return result;
        }
    }

    // [CommandHandler]
    public virtual async Task SetAvatar(IUserAuthorsBackend.SetAvatarCommand command, CancellationToken cancellationToken)
    {
        var userId = command.UserId;

        if (Computed.IsInvalidating()) {
            _ = Get(userId, false, default);
            _ = Get(userId, true, default);
            return;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbUserAuthor= await dbContext.UserAuthors
            .SingleOrDefaultAsync(a => a.UserId == userId, cancellationToken)
            .ConfigureAwait(false);
        if (dbUserAuthor == null)
            throw new InvalidOperationException("user author does not exists");

        dbUserAuthor.AvatarId = command.AvatarId;
        dbUserAuthor.Version = VersionGenerator.NextVersion();

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
