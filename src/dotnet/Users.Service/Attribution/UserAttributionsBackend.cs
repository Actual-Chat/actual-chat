using Microsoft.EntityFrameworkCore;
using ActualChat.Flows;
using ActualChat.Users.Db;
using ActualChat.Users.Flows;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Users;

public class UserAttributionsBackend(IServiceProvider services)
    : ShardedDbServiceBase<UsersDbContext>(services), IUserAttributionsBackend
{
    private FlowHub FlowHub => field ??= Services.FlowHub();

    // [ComputeMethod]
    public virtual async Task<ApiArray<UserAttribution>> List(UserId userId, CancellationToken cancellationToken)
    {
        await ShardOwner.RequireShardOwnership(userId, addDependency: true, cancellationToken).ConfigureAwait(false);

        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var dbAttributions = await dbContext.UserAttributions
            .Where(a => a.UserId == userId.Value)
            .OrderBy(a => a.FirstSeenAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return dbAttributions.Select(a => a.ToModel()).ToApiArray();
    }

    // [CommandHandler]
    public virtual async Task<UserAttribution?> OnAdd(
        UserAttributionsBackend_Add command, CancellationToken cancellationToken)
    {
        var (userId, origin, raw, seenAt) = command;
        if (userId.IsGuest || AttributionParser.Parse(origin, raw) is not { } details)
            return null;

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbAttributions = await dbContext.UserAttributions.ForUpdate()
            .Where(a => a.UserId == userId.Value)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var seenAtTime = seenAt.ToDateTimeClamped();
        var dbAttribution = dbAttributions.FirstOrDefault(a => a.DedupeKey == details.DedupeKey);
        if (dbAttribution is null) {
            dbAttribution = new DbUserAttribution {
                UserId = userId.Value,
                Origin = origin,
                FirstSeenAt = seenAtTime,
                LastSeenAt = seenAtTime,
            };
            dbAttribution.UpdateFrom(details);
            dbAttributions.Add(dbAttribution);
            dbContext.Add(dbAttribution);
        }
        else {
            // Seen again, e.g. a second click on the same keyword: the newer click id is the one that counts
            if (seenAtTime < dbAttribution.FirstSeenAt)
                dbAttribution.FirstSeenAt = seenAtTime;
            if (seenAtTime > dbAttribution.LastSeenAt)
                dbAttribution.LastSeenAt = seenAtTime;
            if (!details.ClickId.IsNullOrEmpty()) {
                dbAttribution.ClickIdKind = details.ClickIdKind;
                dbAttribution.ClickId = details.ClickId;
            }
        }
        dbAttribution.Raw = raw;

        // The flags move in two saves: each partial unique index must hold after every statement
        var initial = dbAttributions.MinBy(a => (a.FirstSeenAt, a.Id == 0 ? long.MaxValue : a.Id))!;
        var latest = dbAttributions.MaxBy(a => (a.LastSeenAt, a.Id == 0 ? long.MaxValue : a.Id))!;
        foreach (var a in dbAttributions) {
            if (a.IsInitial && a != initial)
                a.IsInitial = false;
            if (a.IsLatest && a != latest)
                a.IsLatest = false;
        }
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        initial.IsInitial = true;
        latest.IsLatest = true;
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        Invalidation.Defer(() => _ = List(userId, default));
        CommandContext.GetCurrent().Operation.AddEvent(FlowHub.NewResumeEvent<MilestoneDeliveryFlow>(userId.Value));
        return dbAttribution.ToModel();
    }
}
