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
    public virtual async Task<SharedLocation?> Get(ChatId chatId, SharedLocationId id, CancellationToken cancellationToken)
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
    public virtual async Task<SharedLocation> OnReport(SharedLocationsBackend_Report command, CancellationToken cancellationToken)
    {
        var (id, authorId, point, liveDuration) = command;
        var chatId = authorId.ChatId;
        if (Invalidation.IsActive) {
            _ = Get(chatId, id, default);
            _ = ListLive(chatId, default);
            return null!;
        }

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        // Serialize all of this author's reports so a new live share can't race an in-flight one
        // (create vs create, or a point report resurrecting a just-superseded share).
        await dbContext.SharedLocations.Lock(authorId, cancellationToken).ConfigureAwait(false);

        var now = Clocks.SystemClock.Now;
        var dbSharedLocation = await dbContext.SharedLocations
            .FirstOrDefaultAsync(x => x.Id == id.Value, cancellationToken)
            .ConfigureAwait(false);

        if (dbSharedLocation is null) {
            var duration = liveDuration.Clamp(TimeSpan.Zero, Constants.Location.MaxDuration);
            if (duration > TimeSpan.Zero)
                await SupersedeLiveShares(dbContext, authorId, now, cancellationToken).ConfigureAwait(false);
            var created = new SharedLocation(id, authorId, point, now, now, duration);
            dbContext.Add(new DbSharedLocation(created));
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return created;
        }

        // A report past LiveUntil is ignored so a frozen share keeps its last position.
        var existing = dbSharedLocation.ToModel();
        if (!existing.IsLive(now))
            return existing;

        var updated = existing with { Point = point, ModifiedAt = now };
        dbSharedLocation.UpdateFrom(updated);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return updated;
    }

    // [CommandHandler]
    public virtual async Task OnStop(SharedLocationsBackend_Stop command, CancellationToken cancellationToken)
    {
        // Stop ends a share early — freeze it now so it leaves the live list, last point kept as a pin.
        var (chatId, id) = command;
        if (Invalidation.IsActive) {
            _ = Get(chatId, id, default);
            _ = ListLive(chatId, default);
            return;
        }

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbSharedLocation = await dbContext.SharedLocations.ForUpdate()
            .FirstOrDefaultAsync(x => x.Id == id.Value, cancellationToken)
            .ConfigureAwait(false);
        if (dbSharedLocation is null)
            return;

        var now = Clocks.SystemClock.Now;
        var sharedLocation = dbSharedLocation.ToModel();
        if (sharedLocation.IsLive(now)) {
            dbSharedLocation.UpdateFrom(sharedLocation with { StoppedAt = now });
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    // Private methods

    private static async Task SupersedeLiveShares(
        ChatDbContext dbContext,
        AuthorId authorId,
        Moment now,
        CancellationToken cancellationToken)
    {
        // One live location per author per chat: a new one supersedes the previous.
        // The caller holds the per-author lock, so a plain read is enough here.
        var dbShares = await dbContext.SharedLocations
            .Where(x => x.AuthorId == authorId.Value && x.StoppedAt == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        // StoppedAt == null still includes expired shares, so the IsLive check stays.
        foreach (var dbShare in dbShares) {
            var share = dbShare.ToModel();
            if (share.IsLive(now))
                dbShare.UpdateFrom(share with { StoppedAt = now });
        }
    }
}
