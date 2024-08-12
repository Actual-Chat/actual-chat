using ActualChat.Db;
using ActualChat.Jobs;
using ActualChat.Queues;
using ActualChat.Users.Db;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;
using TimeZoneConverter;

namespace ActualChat.Users.Jobs;

internal sealed class DigestJob(IServiceProvider services)
    : DbServiceBase<UsersDbContext>(services), IJob
{
    private const int DigestTime = 9; // 9 AM
    private const int PageSize = 10000;

    private IQueues Queues { get; } = services.Queues();

    public async Task Run(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var timeZones = await dbContext.Accounts
            .Select(x => x.TimeZone)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var selectedTimeZones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var timeZone in timeZones) {
            if (!TZConvert.TryGetTimeZoneInfo(timeZone, out var timeZoneInfo)) {
                Log.LogWarning("Unable to find time zone info. Time zone: '{TimeZone}'.", timeZone);
                continue;
            }
            var nowInTimeZone = TimeZoneInfo.ConvertTime(now, timeZoneInfo);
            if (nowInTimeZone.Hour != DigestTime)
                continue;
            selectedTimeZones.Add(timeZone);
        }

        if (selectedTimeZones.Count == 0)
            return;

        var accountIds = dbContext.Accounts
            .Where(x => selectedTimeZones.Contains(x.TimeZone))
            .Where(x => x.Email.Contains("@actual.chat"))
            .OrderBy(x => x.Id)
            .ReadAsync(PageSize, x => x.Id, cancellationToken);
        await foreach (var accountId in accountIds.ConfigureAwait(false)) {
            var userId = UserId.Parse(accountId);
            var sendDigestCommand = new Emails_SendDigest(userId);
            await Queues.Enqueue(sendDigestCommand, cancellationToken).ConfigureAwait(false);
        }
    }
}
