using ActualChat.Flows;
using ActualChat.Users.Db;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Users.Flows;

/// <summary>
/// A flow that iterates through all accounts and:
/// - Triggers a fake update on each one (touch).
/// - Starts a <see cref="DigestFlow"/> for each one.
/// Processes accounts in batches, all items in a batch are processed in parallel.
/// </summary>
[Flow(DataVersion = 1, DelayQuanta = 0)]
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public partial class AccountMigrationFlow : Flow<(Moment, long)>
{
    private const int BatchSize = 50;
    private static readonly RandomTimeSpan BatchDelay = TimeSpan.FromSeconds(5).ToRandom(0.25);

    private DbHub<UsersDbContext> DbHub => field ??= Services.DbHub<UsersDbContext>();
    private IAccountsBackend AccountsBackend => field ??= Services.GetRequiredService<IAccountsBackend>();
    private ICommander Commander => Hub.Commander;

    [DataMember(Order = 0), MemoryPackOrder(0), Key(0)]
    public string? LastProcessedId { get; set; }
    [DataMember(Order = 1), MemoryPackOrder(1), Key(1)]
    public int MigratedCount { get; set; }
    [DataMember(Order = 2), MemoryPackOrder(2), Key(2)]
    public int TotalCount { get; set; }

    protected override async ValueTask Resume(CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        if (TotalCount == 0)
            TotalCount = Math.Max(1, await dbContext.Accounts
                .CountAsync(cancellationToken)
                .ConfigureAwait(false));

        // Get the next batch of accounts
        var query = dbContext.Accounts
            .OrderBy(x => x.Id)
            .AsQueryable();

        if (!LastProcessedId.IsNullOrEmpty())
            query = query.Where(x => string.Compare(x.Id, LastProcessedId) > 0);

        var batch = await query
            .Take(BatchSize)
            .Select(x => new { x.Id, x.Version })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (batch.Count == 0) {
            Complete();
            return;
        }

        // Process all accounts in the batch in parallel
        await batch
            .Select(x => ProcessOne(UserId.Parse(x.Id), cancellationToken))
            .Collect(cancellationToken)
            .ConfigureAwait(false);

        LastProcessedId = batch[^1].Id;
        MigratedCount += batch.Count;

        var progress = (double)MigratedCount / TotalCount * 100;
        Console.Log($"Progress: {progress:F1}% ({MigratedCount}/{TotalCount} accounts)");

        if (batch.Count < BatchSize) {
            Complete();
            return;
        }

        // Schedule next batch after delay
        Runtime.StageResumeIn(BatchDelay.Next());
    }

    private void Complete()
    {
        Console.Log($"Completed, processed {MigratedCount} accounts");
        SetResult((Hub.Clocks.SystemClock.Now, MigratedCount));
    }

    private async Task<bool> ProcessOne(UserId userId, CancellationToken cancellationToken)
    {
        var account = await AccountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);
        if (account is null)
            return false; // Already removed

        // Start DigestFlow
        await Hub.NewResumeEvent<DigestFlow>(userId.Value)
            .WithDelayQuanta(TimeSpan.Zero) // Otherwise tests may fail due to cross-test NATS dedup!
            .Schedule(cancellationToken)
            .ConfigureAwait(false);

        if (account.FormatVersion >= 2)
            return false;

        // Requires an upgrade, AccountsBackend_Update does this
        var updateCommand = new AccountsBackend_Update(account, account.Version);
        await Commander.Call(updateCommand, cancellationToken).ConfigureAwait(false);
        return true;
    }
}
