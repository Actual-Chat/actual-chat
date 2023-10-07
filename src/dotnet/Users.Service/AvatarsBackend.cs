using ActualChat.Commands;
using ActualChat.Media;
using ActualChat.Users.Db;
using ActualChat.Users.Events;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Users;

public class AvatarsBackend(IServiceProvider services) : DbServiceBase<UsersDbContext>(services), IAvatarsBackend
{
    private IDbEntityResolver<string, DbAvatar> DbAvatarResolver { get; }
        = services.GetRequiredService<IDbEntityResolver<string, DbAvatar>>();
    private IMediaBackend MediaBackend { get; }
        = services.GetRequiredService<IMediaBackend>();

    // [ComputeMethod]
    public virtual async Task<AvatarFull?> Get(Symbol avatarId, CancellationToken cancellationToken)
    {
        if (avatarId.IsEmpty)
            return null;

        var dbUserAvatar = await DbAvatarResolver.Get(avatarId, cancellationToken).ConfigureAwait(false);
        var userAvatar = dbUserAvatar?.ToModel();
        if (userAvatar == null)
            return null;

        if (userAvatar.MediaId.IsNone)
            return userAvatar;

        var media = await MediaBackend.Get(userAvatar.MediaId, cancellationToken).ConfigureAwait(false);
        return userAvatar with { Media = media };
    }

    // [CommandHandler]
    public virtual async Task<AvatarFull> OnChange(AvatarsBackend_Change command, CancellationToken cancellationToken)
    {
        var (avatarId, expectedVersion, change) = command;
        if (Computed.IsInvalidating()) {
            if (!avatarId.IsEmpty)
                _ = Get(avatarId, default);
            return default!;
        }

        change.RequireValid();
        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        AvatarFull? existingAvatar = null;
        if (change.IsCreate(out var avatar)) {
            avatar = avatar with {
                Id = DbAvatar.IdGenerator.Next(),
                Version = VersionGenerator.NextVersion(),
            };
            var dbAvatar = new DbAvatar(avatar);
            dbContext.Avatars.Add(dbAvatar);
        }
        else {
            var dbAvatar = await dbContext.Avatars
                .Get(avatarId, cancellationToken)
                .RequireVersion(expectedVersion)
                .ConfigureAwait(false);
            existingAvatar = dbAvatar.ToModel();

            if (change.IsUpdate(out avatar)) {
                avatar = avatar with {
                    Version = VersionGenerator.NextVersion(avatar.Version),
                };
                dbAvatar.UpdateFrom(avatar);
            }
            else {
                avatar = dbAvatar.ToModel();
                dbContext.Remove(dbAvatar);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Raise events
        new AvatarChangedEvent(avatar, existingAvatar, change.Kind)
            .EnqueueOnCompletion();
        return avatar;
    }
}
