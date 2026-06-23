using ActualChat.Chat.Db;
using ActualChat.Db;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Chat;

public class LiveLocationsBackend(IServiceProvider services)
    : DbServiceBase<ChatDbContext>(services), ILiveLocationsBackend
{
    // [ComputeMethod]
    public virtual async Task<LiveLocation?> Get(ChatId chatId, AuthorId authorId, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var id = DbLiveLocation.ComposeId(chatId, authorId);
        var dbLiveLocation = await dbContext.LiveLocations.Get(id, cancellationToken).ConfigureAwait(false);
        return dbLiveLocation == null ? null : Active(dbLiveLocation.ToModel());
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<LiveLocation>> List(ChatId chatId, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var dbLiveLocations = await dbContext.LiveLocations
            .Where(x => x.ChatId == chatId.Value)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var now = Clocks.SystemClock.Now;
        var result = new List<LiveLocation>();
        Moment? soonestExpiry = null;
        foreach (var dbLiveLocation in dbLiveLocations) {
            var model = dbLiveLocation.ToModel();
            var activeUntil = ActiveUntil(model);
            if (activeUntil <= now)
                continue;

            result.Add(model);
            if (soonestExpiry is not { } value || activeUntil < value)
                soonestExpiry = activeUntil;
        }
        if (soonestExpiry is { } expiry)
            Computed.GetCurrent().Invalidate(expiry - now);
        return result.ToApiArray();
    }

    // [CommandHandler]
    public virtual async Task OnReport(LiveLocationsBackend_Report command, CancellationToken cancellationToken)
    {
        var (chatId, authorId, point, duration) = command;
        if (Invalidation.IsActive) {
            _ = Get(chatId, authorId, default);
            _ = List(chatId, default);
            return;
        }

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var now = Clocks.SystemClock.Now;
        var id = DbLiveLocation.ComposeId(chatId, authorId);
        var dbLiveLocation = await dbContext.LiveLocations.ForUpdate()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            .ConfigureAwait(false);

        // A position-only report (no duration) updates an active share but never starts one.
        if (dbLiveLocation == null && duration == null)
            return;

        if (dbLiveLocation == null) {
            dbLiveLocation = new DbLiveLocation {
                Id = id,
                ChatId = chatId.Value,
                AuthorId = authorId.Value,
                CreatedAt = now,
            };
            dbContext.Add(dbLiveLocation);
        }
        else if (duration != null)
            // A (re)start resets the share window so the new duration measures from now.
            dbLiveLocation.CreatedAt = now;

        dbLiveLocation.Latitude = point.Latitude;
        dbLiveLocation.Longitude = point.Longitude;
        dbLiveLocation.Accuracy = point.Accuracy;
        dbLiveLocation.Bearing = point.Bearing;
        dbLiveLocation.ModifiedAt = now;
        if (duration is { } value)
            dbLiveLocation.Duration = value.Clamp(Constants.LiveLocation.MinDuration, Constants.LiveLocation.MaxDuration);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnStop(LiveLocationsBackend_Stop command, CancellationToken cancellationToken)
    {
        var (chatId, authorId) = command;
        if (Invalidation.IsActive) {
            _ = Get(chatId, authorId, default);
            _ = List(chatId, default);
            return;
        }

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var id = DbLiveLocation.ComposeId(chatId, authorId);
        var dbLiveLocation = await dbContext.LiveLocations.ForUpdate()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (dbLiveLocation == null)
            return;

        // Removing the row scrubs the coordinates so a stopped share leaves no residual position
        dbContext.Remove(dbLiveLocation);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private LiveLocation? Active(LiveLocation model)
    {
        var now = Clocks.SystemClock.Now;
        var activeUntil = ActiveUntil(model);
        if (activeUntil <= now)
            return null;

        Computed.GetCurrent().Invalidate(activeUntil - now);
        return model;
    }

    private static Moment ActiveUntil(LiveLocation model)
    {
        var staleUntil = model.ModifiedAt + Constants.LiveLocation.StaleTimeout;
        return model.ExpiresAt < staleUntil ? model.ExpiresAt : staleUntil;
    }

    private static TimeSpan Duration(TimeSpan duration)
    {
        var maxDuration = Constants.LiveLocation.MaxDuration;
        if (duration <= TimeSpan.Zero || duration > maxDuration)
            return maxDuration;

        return duration;
    }
}
