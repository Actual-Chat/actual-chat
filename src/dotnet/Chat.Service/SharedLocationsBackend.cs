using ActualChat.Chat.Db;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;

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
    public virtual async Task<ApiArray<SharedLocation>> List(ChatId chatId, CancellationToken cancellationToken)
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
    public virtual async Task<SharedLocation> OnCreate(SharedLocationsBackend_Create command, CancellationToken cancellationToken)
    {
        var (id, chatId, authorId, point, liveDuration) = command;
        if (Invalidation.IsActive) {
            _ = Get(chatId, id, default);
            _ = List(chatId, default);
            return null!;
        }

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var now = Clocks.SystemClock.Now;
        var dbSharedLocation = new DbSharedLocation {
            Id = id.Value,
            ChatId = chatId.Value,
            AuthorId = authorId.Value,
            Latitude = point.Latitude,
            Longitude = point.Longitude,
            Accuracy = point.Accuracy,
            Bearing = point.Bearing,
            CreatedAt = now,
            ModifiedAt = now,
            Duration = liveDuration.Clamp(TimeSpan.Zero, Constants.Location.MaxDuration),
        };
        dbContext.Add(dbSharedLocation);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return dbSharedLocation.ToModel();
    }

    // [CommandHandler]
    public virtual async Task OnReport(SharedLocationsBackend_Report command, CancellationToken cancellationToken)
    {
        var (chatId, id, point) = command;
        if (Invalidation.IsActive) {
            _ = Get(chatId, id, default);
            _ = List(chatId, default);
            return;
        }

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbSharedLocation = await dbContext.SharedLocations.ForUpdate()
            .FirstOrDefaultAsync(x => x.Id == id.Value, cancellationToken)
            .ConfigureAwait(false);
        // TODO: tomodel and use model below. probably use UpdateFrom?
        if (dbSharedLocation == null)
            return;

        // A report past LiveUntil is ignored so a frozen share keeps its last position.
        var now = Clocks.SystemClock.Now;
        if (now >= dbSharedLocation.CreatedAt.ToMoment() + dbSharedLocation.Duration)
            return;

        dbSharedLocation.Latitude = point.Latitude;
        dbSharedLocation.Longitude = point.Longitude;
        dbSharedLocation.Accuracy = point.Accuracy;
        dbSharedLocation.Bearing = point.Bearing;
        dbSharedLocation.ModifiedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    // TODO: why do we need stop at all???
    // [CommandHandler]
    public virtual async Task OnStop(SharedLocationsBackend_Stop command, CancellationToken cancellationToken)
    {
        var (chatId, id) = command;
        if (Invalidation.IsActive) {
            _ = Get(chatId, id, default);
            _ = List(chatId, default);
            return;
        }

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbSharedLocation = await dbContext.SharedLocations.ForUpdate()
            .FirstOrDefaultAsync(x => x.Id == id.Value, cancellationToken)
            .ConfigureAwait(false);
        if (dbSharedLocation == null)
            return;

        // Freeze immediately: the last position stays as a static pin in history.
        var now = Clocks.SystemClock.Now;
        var createdAt = dbSharedLocation.CreatedAt.ToMoment();
        if (createdAt + dbSharedLocation.Duration > now) {
            dbSharedLocation.Duration = now - createdAt;
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
