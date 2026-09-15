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
        if (sharedLocation.IsLive(now) && !sharedLocation.IsUnlimited)
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
        var result = dbSharedLocations
            .Select(x => x.ToModel())
            .Where(x => x.IsLive(now))
            .ToApiArray();
        var soonestExpiry = result
            .Where(x => !x.IsUnlimited)
            .Min(x => (Moment?)x.LiveUntil);
        if (soonestExpiry is { } expiry)
            Computed.GetCurrent().Invalidate(expiry - now);
        return result;
    }

    // [CommandHandler]
    public virtual async Task<SharedLocation?> OnChange(
        SharedLocationsBackend_Change command,
        CancellationToken cancellationToken)
    {
        var (id, authorId, change) = command;
        var chatId = authorId.ChatId;
        var context = CommandContext.GetCurrent();
        if (Invalidation.IsActive) {
            // The created id is minted below and a takeover freezes rows this command never named,
            // so the affected set is read back from the operation.
            var invLocations = context.Operation.Items.KeylessGet<ApiArray<SharedLocation>>();
            foreach (var invLocation in invLocations)
                _ = Get(invLocation.Id, default);
            if (!invLocations.IsEmpty)
                _ = ListLive(chatId, default);
            return null!;
        }

        change.RequireValid();
        var isCreate = change.IsCreate(out var createDiff);
        if (!isCreate && id is { } existingId) {
            // A device that lost its share to a takeover keeps pushing into the frozen row - once per
            // UpdatePeriod, indefinitely - so it's turned away before the operation and the author lock
            // are paid for. Read through Get so those pushes hit the cache rather than the DB.
            var existing = await Get(existingId, cancellationToken).ConfigureAwait(false);
            if (existing is not null) {
                RequireOwnedBy(existing, authorId);
                if (!existing.IsLive(Clocks.SystemClock.Now))
                    return existing;
            }
        }

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
        if (sharedLocation is not null)
            RequireOwnedBy(sharedLocation, authorId);

        var affected = ApiArray<SharedLocation>.Empty;
        if (isCreate) {
            // The front-end OnChange restricts the duration to the menu options; backend callers are trusted
            var duration = createDiff.LiveDuration ?? TimeSpan.Zero;
            if (duration > TimeSpan.Zero) {
                // The newest share wins: whatever this author had live in this chat is frozen right here,
                // so the device that owned it can't keep reporting into a row it no longer owns.
                affected = await StopOwnLiveShares(dbContext, authorId, now, cancellationToken).ConfigureAwait(false);
                var liveCount = await CountChatLiveShares(dbContext, authorId, now, cancellationToken)
                    .ConfigureAwait(false);
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
                IsPlace = createDiff.IsPlace,
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
        context.Operation.Items.KeylessSet(affected.With(sharedLocation));
        return sharedLocation;
    }

    // Private methods

    private static void RequireOwnedBy(SharedLocation sharedLocation, AuthorId authorId)
    {
        if (sharedLocation.AuthorId != authorId)
            throw StandardError.Unauthorized("You can change only your own shared locations in this chat.");
    }

    private async Task<ApiArray<SharedLocation>> StopOwnLiveShares(
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
        var stopped = ApiArray<SharedLocation>.Empty;
        foreach (var dbShare in dbShares) {
            // StoppedAt == null still includes expired shares, which are frozen already.
            var share = dbShare.ToModel();
            if (!share.IsLive(now))
                continue;

            share = share with {
                StoppedAt = now,
                Version = VersionGenerator.NextVersion(share.Version),
            };
            dbShare.UpdateFrom(share);
            stopped = stopped.With(share);
        }
        return stopped;
    }

    private static Task<int> CountChatLiveShares(
        ChatDbContext dbContext,
        AuthorId authorId,
        Moment now,
        CancellationToken cancellationToken)
    {
        var chatId = authorId.ChatId;
        var nowUtc = now.ToDateTime();
        return dbContext.SharedLocations
            .CountAsync(
                x => x.ChatId == chatId.Value
                    && x.AuthorId != authorId.Value
                    && x.StoppedAt == null
                    && x.CreatedAt + x.Duration > nowUtc,
                cancellationToken);
    }
}
