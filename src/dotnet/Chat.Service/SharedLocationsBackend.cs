using ActualChat.Chat.Db;
using ActualChat.Db;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat;

public class SharedLocationsBackend(IServiceProvider services)
    : DbServiceBase<ChatDbContext>(services), ISharedLocationsBackend
{
    private IDbEntityResolver<string, DbSharedLocation> DbSharedLocationResolver
        => field ??= Services.GetRequiredService<IDbEntityResolver<string, DbSharedLocation>>();

    // [ComputeMethod]
    public virtual async Task<SharedLocation?> Get(SharedLocationId id, CancellationToken cancellationToken)
    {
        var dbSharedLocation = await DbSharedLocationResolver.Get(id.Value, cancellationToken).ConfigureAwait(false);
        var sharedLocation = dbSharedLocation?.ToModel();
        if (sharedLocation is null)
            return null;

        var now = Clocks.SystemClock.Now;
        if (sharedLocation.IsLive(now))
            Computed.GetCurrent().Invalidate(sharedLocation.LiveUntil - now);
        return sharedLocation;
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<SharedLocation>> ListLive(ChatId chatId, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var dbSharedLocations = await dbContext.SharedLocations
            .Where(x => x.ChatId == chatId.Value)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var now = Clocks.SystemClock.Now;
        var result = new List<SharedLocation>();
        Moment? soonestExpiry = null;
        foreach (var dbSharedLocation in dbSharedLocations) {
            var model = dbSharedLocation.ToModel();
            if (!model.IsLive(now))
                continue;

            result.Add(model);
            if (soonestExpiry is not { } value || model.LiveUntil < value)
                soonestExpiry = model.LiveUntil;
        }
        if (soonestExpiry is { } expiry)
            Computed.GetCurrent().Invalidate(expiry - now);
        return result.ToApiArray();
    }

    // [CommandHandler]
    public virtual async Task<SharedLocation?> OnChange(SharedLocationsBackend_Change command, CancellationToken cancellationToken)
    {
        var (id, authorId, change) = command;
        var chatId = authorId.ChatId;
        var context = CommandContext.GetCurrent();
        if (Invalidation.IsActive) {
            // The created id is minted below, so read the affected share back from the operation.
            if (context.Operation.Items.KeylessGet<SharedLocation>() is { } invLocation) {
                _ = Get(invLocation.Id, default);
                _ = ListLive(invLocation.ChatId, default);
            }
            return null!;
        }

        change.RequireValid();
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        // Serialize this author's changes so concurrent creates can't both mint a live share.
        await dbContext.SharedLocations.Lock(authorId, cancellationToken).ConfigureAwait(false);
        var now = Clocks.SystemClock.Now;

        // Update/Remove act on the existing share; Create mints a fresh one, so it has no id to load by.
        var dbSharedLocation = id is null
            ? null
            : await dbContext.SharedLocations
                .FirstOrDefaultAsync(x => x.Id == id.Value, cancellationToken)
                .ConfigureAwait(false);
        var sharedLocation = dbSharedLocation?.ToModel();

        if (change.IsCreate(out var createDiff)) {
            var duration = createDiff.LiveDuration?.Clamp(TimeSpan.Zero, Constants.Location.MaxDuration)
                ?? TimeSpan.Zero;
            if (duration > TimeSpan.Zero) {
                // One live share per author: hand back the running one instead of starting a second.
                var live = await GetOwnLiveShare(dbContext, authorId, now, cancellationToken).ConfigureAwait(false);
                if (live is not null)
                    return live;

                var liveCount = await CountLiveShares(dbContext, chatId, now, cancellationToken).ConfigureAwait(false);
                if (liveCount >= Constants.Location.MaxSharingAuthorsPerChat)
                    throw StandardError.Constraint(
                        $"This chat already has the maximum of {Constants.Location.MaxSharingAuthorsPerChat} "
                        + "people sharing their live location.");
            }

            sharedLocation = new SharedLocation(SharedLocationId.New(), VersionGenerator.NextVersion()) {
                AuthorId = authorId,
                Point = createDiff.Point.Require(),
                CreatedAt = now,
                ModifiedAt = now,
                Duration = duration,
            };
            dbContext.Add(new DbSharedLocation(sharedLocation));
        }
        else if (change.IsUpdate(out var updateDiff)) {
            // A change past LiveUntil is ignored so a frozen share keeps its last position.
            if (sharedLocation is null || !sharedLocation.IsLive(now))
                return sharedLocation;

            // Update moves the point.
            sharedLocation = sharedLocation with {
                Point = updateDiff.Point ?? sharedLocation.Point,
                ModifiedAt = now,
                Version = VersionGenerator.NextVersion(sharedLocation.Version),
            };
            dbSharedLocation!.UpdateFrom(sharedLocation);
        }
        else {
            if (sharedLocation is null || !sharedLocation.IsLive(now))
                return sharedLocation;

            // Remove stops the share: freeze it, last point kept as a pin.
            sharedLocation = sharedLocation with {
                StoppedAt = now,
                Version = VersionGenerator.NextVersion(sharedLocation.Version),
            };
            dbSharedLocation!.UpdateFrom(sharedLocation);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.Operation.Items.KeylessSet(sharedLocation);
        return sharedLocation;
    }

    // Private methods

    private static async Task<SharedLocation?> GetOwnLiveShare(
        ChatDbContext dbContext,
        AuthorId authorId,
        Moment now,
        CancellationToken cancellationToken)
    {
        // The caller holds the per-author lock, so a plain read is enough here.
        var dbShares = await dbContext.SharedLocations
            .Where(x => x.AuthorId == authorId.Value && x.StoppedAt == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        // StoppedAt == null still includes expired shares, so the IsLive check stays.
        return dbShares.Select(x => x.ToModel()).FirstOrDefault(x => x.IsLive(now));
    }

    private static Task<int> CountLiveShares(
        ChatDbContext dbContext,
        ChatId chatId,
        Moment now,
        CancellationToken cancellationToken)
    {
        var nowUtc = now.ToDateTime();
        return dbContext.SharedLocations
            .CountAsync(
                // TODO: ensure required db index declared
                x => x.ChatId == chatId.Value && x.StoppedAt == null && x.CreatedAt + x.Duration > nowUtc,
                cancellationToken);
    }
}
