using ActualChat.Db;
using ActualChat.Users.Db;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Users;

public class UserVoicesBackend(IServiceProvider services)
    : DbServiceBase<UsersDbContext>(services), IUserVoicesBackend
{
    private IDbEntityResolver<string, DbUserVoice> DbUserVoiceResolver { get; }
        = services.GetRequiredService<IDbEntityResolver<string, DbUserVoice>>();
    private DiffEngine DiffEngine { get; } = services.GetRequiredService<DiffEngine>();

    // [ComputeMethod]
    public virtual async Task<UserVoice?> Get(UserId userId, CancellationToken cancellationToken)
    {
        var dbUserVoice = await DbUserVoiceResolver.Get(userId.Value, cancellationToken).ConfigureAwait(false);
        return dbUserVoice?.ToModel();
    }

    public async Task<ApiArray<UserVoice>> ListActive(CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbUserVoices = await dbContext.UserVoices
            .Where(x => x.Status == UserVoiceStatus.Creating || x.Status == UserVoiceStatus.Ready)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return dbUserVoices.Select(x => x.ToModel()).ToApiArray();
    }

    // [CommandHandler]
    public virtual async Task<UserVoice?> OnChange(UserVoicesBackend_Change command, CancellationToken cancellationToken)
    {
        var (userId, expectedVersion, change) = command;
        if (Invalidation.IsActive) {
            _ = Get(userId, default);
            return default!;
        }

        change.RequireValid();
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        await dbContext.UserVoices.Lock(userId.Value, cancellationToken).ConfigureAwait(false);
        var dbUserVoice = await dbContext.UserVoices
            .FirstOrDefaultAsync(x => x.Id == userId.Value, cancellationToken)
            .ConfigureAwait(false);

        UserVoice? userVoice;
        if (change.IsCreate(out var create)) {
            if (dbUserVoice != null)
                return dbUserVoice.ToModel();

            userVoice = DiffEngine.Patch(new UserVoice(userId), create) with {
                Version = VersionGenerator.NextVersion(),
            };
            dbContext.Add(new DbUserVoice(userVoice));
        }
        else if (change.IsUpdate(out var update)) {
            dbUserVoice.RequireVersion(expectedVersion);
            userVoice = DiffEngine.Patch(dbUserVoice.ToModel(), update) with {
                Version = VersionGenerator.NextVersion(dbUserVoice.Version),
            };
            dbUserVoice.UpdateFrom(userVoice);
        }
        else if (change.IsRemove()) {
            dbUserVoice.RequireVersion(expectedVersion);
            dbContext.Remove(dbUserVoice);
            userVoice = null;
        }
        else
            throw new NotSupportedException("Invalid change.");

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return userVoice;
    }
}
