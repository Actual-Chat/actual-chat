using ActualChat.Chat.Db;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Chat;

public sealed class LiveLocationsCleanup : ActivatedWorkerBase
{
    private DbHub<ChatDbContext> DbHub { get; }
    private MomentClockSet Clocks { get; }

    public LiveLocationsCleanup(IServiceProvider services) : base(services)
    {
        DbHub = services.DbHub<ChatDbContext>();
        Clocks = services.Clocks();
        UnconditionalActivationPeriod = TimeSpan.FromMinutes(1).ToRandom(0.1);
    }

    protected override async Task<bool> OnActivate(CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(readWrite: true, cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        DateTime now = Clocks.SystemClock.Now;
        await dbContext.LiveLocations
            .Where(x => x.CreatedAt + x.Duration < now)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
        return true;
    }
}
