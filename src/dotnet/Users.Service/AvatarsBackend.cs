using ActualChat.Users.Db;
using Stl.Fusion.EntityFramework;
using Stl.Generators;
using Stl.Versioning;

namespace ActualChat.Users;

public class AvatarsBackend : DbServiceBase<UsersDbContext>, IAvatarsBackend
{
    private IAuth Auth { get; }
    private IAccountsBackend AccountsBackend { get; }
    private IServerKvasBackend ServerKvasBackend { get; }
    private IDbEntityResolver<string, DbAvatar> DbAvatarResolver { get; }
    private RandomStringGenerator AvatarIdGenerator { get; }

    public AvatarsBackend(IServiceProvider services) : base(services)
    {
        AccountsBackend = services.GetRequiredService<IAccountsBackend>();
        ServerKvasBackend = services.GetRequiredService<IServerKvasBackend>();
        DbAvatarResolver = services.GetRequiredService<IDbEntityResolver<string, DbAvatar>>();
        AvatarIdGenerator = new RandomStringGenerator(12, RandomStringGenerator.Base32Alphabet);
    }

    // [ComputeMethod]
    public virtual async Task<AvatarFull?> Get(string avatarId, CancellationToken cancellationToken)
    {
        if (avatarId.IsNullOrEmpty())
            return null;

        var dbUserAvatar = await DbAvatarResolver.Get(avatarId, cancellationToken).ConfigureAwait(false);
        var userAvatar = dbUserAvatar?.ToModel();
        return userAvatar;
    }

    // [CommandHandler]
    public virtual async Task<AvatarFull> Change(IAvatarsBackend.ChangeCommand command, CancellationToken cancellationToken)
    {
        var (avatarId, change) = command;
        if (Computed.IsInvalidating()) {
            if (!avatarId.IsNullOrEmpty())
                _ = Get(avatarId, default);
            return default!;
        }

        change.RequireValid();
        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        if (change.IsCreate(out var avatar)) {
            avatar = avatar with {
                Id = AvatarIdGenerator.Next(),
                Version = VersionGenerator.NextVersion(),
            };
            var dbAvatar = new DbAvatar(avatar);
            dbContext.Avatars.Add(dbAvatar);
        }
        else {
            var dbAvatar = await dbContext.Avatars.FindAsync(DbKey.Compose(avatarId)).ConfigureAwait(false);
            if (dbAvatar == null)
                throw StandardError.NotFound<Avatar>();
            VersionChecker.RequireExpected(dbAvatar.Version, avatar.Version);

            if (change.IsUpdate(out avatar)) {
                avatar = avatar with { Version = VersionGenerator.NextVersion(avatar.Version) };
                dbAvatar.UpdateFrom(avatar);
            }
            else
                dbContext.Remove(dbAvatar);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return avatar;
    }
}
