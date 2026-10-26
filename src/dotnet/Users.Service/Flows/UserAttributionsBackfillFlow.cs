using ActualChat.Flows;
using ActualChat.Users.Db;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Users.Flows;

/// <summary>
/// Gives each user who signed up before attributions were stored the one their <see cref="UsageEventKind.SignUp"/>
/// event recorded. Its time is the sign-up's, so it becomes the user's initial attribution.
/// </summary>
[Flow(DataVersion = 1, DelayQuanta = 0)]
[DataContract, MessagePackObject(true)]
public sealed partial class UserAttributionsBackfillFlow : Flow<(Moment, long)>
{
    private const int BatchSize = 200;
    private static readonly RandomTimeSpan BatchDelay = TimeSpan.FromSeconds(1).ToRandom(0.25);
    private static readonly TimeSpan StallTimeout = TimeSpan.FromMinutes(5);

    private DbHub<UsersDbContext> DbHub => field ??= Services.DbHub<UsersDbContext>();
    private ICommander Commander => Hub.Commander;

    [DataMember(Order = 0), Key(0)]
    public string? LastProcessedUserId { get; set; }
    [DataMember(Order = 1), Key(1)]
    public long AddedCount { get; set; }
    [DataMember(Order = 2), Key(2)]
    public long ScannedCount { get; set; }
    [DataMember(Order = 3), Key(3)]
    public Moment NextBatchAt { get; set; }

    // Protected/internal methods

    protected override async ValueTask Resume(CancellationToken cancellationToken)
    {
        // MigrationFlow resumes it on every start; batches chain through its own resumes, so a second chain
        // must not start while one runs
        if (!ResumeEvent.IsSelfResume && NextBatchAt != default && ResumedAt < NextBatchAt + StallTimeout) {
            Console.Log("Backfill in progress, an external resume is ignored");
            return;
        }

        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        // Plain "store" and "web" sign-ups attribute nothing, so only these kinds are read
        var query = dbContext.UsageEvents.Where(e => e.Kind == UsageEventKind.SignUp
            && (e.SourceId.StartsWith("campaign:")
                || e.SourceId.StartsWith("join:")
                || e.SourceId.StartsWith("user:")));
        if (!LastProcessedUserId.IsNullOrEmpty())
            query = query.Where(e => string.Compare(e.UserId, LastProcessedUserId) > 0);

        var batch = await query
            .OrderBy(e => e.UserId)
            .Take(BatchSize)
            .Select(e => new { e.UserId, e.SourceId, e.OccurredAt })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (batch.Count == 0) {
            Console.Log($"Completed: {AddedCount} attributions from {ScannedCount} sign-ups");
            SetResult((Hub.Clocks.SystemClock.Now, AddedCount));
            return;
        }

        foreach (var row in batch) {
            if (!UserId.TryParse(row.UserId, out var userId) || userId.IsGuest)
                continue;

            var addCmd = new UserAttributionsBackend_Add(
                userId, AttributionOrigin.LegacySignUp, row.SourceId, row.OccurredAt.ToMoment());
            var attribution = await Commander.Call(addCmd, cancellationToken).ConfigureAwait(false);
            if (attribution is not null)
                AddedCount++;
        }
        LastProcessedUserId = batch[^1].UserId;
        ScannedCount += batch.Count;
        Console.Log($"Progress: {AddedCount} attributions from {ScannedCount} sign-ups");
        NextBatchAt = Hub.SystemNow + BatchDelay.Next();
        Runtime.StageResumeAt(NextBatchAt, TimeSpan.Zero);
    }
}
