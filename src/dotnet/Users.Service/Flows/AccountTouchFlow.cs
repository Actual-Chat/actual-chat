using ActualChat.Flows;
using ActualChat.Users.Db;
using ActualLab.Fusion.EntityFramework;
using MemoryPack;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Users.Flows;

/// <summary>
/// A flow that iterates through all accounts and triggers a fake update on each one.
/// Processes 600 accounts per batch, then sleeps for 1 minute (10 accounts/second average).
/// </summary>
[Flow(DataVersion = 2)]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class AccountTouchFlow : Flow<Unit>
{
    private const int BatchSize = 600;
    private static readonly TimeSpan BatchDelay = TimeSpan.FromMinutes(1);

    [DataMember(Order = 0), MemoryPackOrder(0)]
    public string? LastProcessedId { get; set; }

    [DataMember(Order = 1), MemoryPackOrder(1)]
    public int TotalProcessed { get; set; }

    protected override async ValueTask Resume(CancellationToken cancellationToken)
    {
        var dbHub = Services.DbHub<UsersDbContext>();
        var accountsBackend = Services.GetRequiredService<IAccountsBackend>();
        var commander = Services.Commander();

        var dbContext = await dbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        // Get the next batch of accounts
        var query = dbContext.Accounts
            .Where(x => !Constants.User.SystemUserIdValues.Contains(x.Id))
            .OrderBy(x => x.Id);

        if (!LastProcessedId.IsNullOrEmpty())
            query = (IOrderedQueryable<DbAccount>)query.Where(x => string.Compare(x.Id, LastProcessedId) > 0);

        var batch = await query
            .Take(BatchSize)
            .Select(x => new { x.Id, x.Version })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (batch.Count == 0) {
            Console.Log($"AccountTouchFlow completed. Total processed: {TotalProcessed}");
            SetResult(Unit.Default);
            return;
        }

        // Process each account in the batch
        var processedInBatch = 0;
        foreach (var item in batch) {
            try {
                var userId = UserId.Parse(item.Id);
                var account = await accountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);
                if (account == null) {
                    Console.Log($"Account {userId} not found, skipping");
                    continue;
                }

                // Trigger update with the same data (fake update)
                var updateCommand = new AccountsBackend_Update(account, account.Version);
                await commander.Call(updateCommand, cancellationToken).ConfigureAwait(false);
                processedInBatch++;
            }
            catch (Exception e) {
                Console.LogWarning($"Failed to touch account {item.Id}: {e.Message}");
            }
        }

        LastProcessedId = batch[^1].Id;
        TotalProcessed += processedInBatch;

        Console.Log($"Processed {processedInBatch} accounts in batch, total: {TotalProcessed}, last ID: {LastProcessedId}");

        if (batch.Count < BatchSize) {
            // We've reached the end
            Console.Log($"AccountTouchFlow completed. Total processed: {TotalProcessed}");
            SetResult(Unit.Default);
            return;
        }

        // Schedule next batch after delay
        Console.Log($"Scheduling next batch in {BatchDelay}");
        Runtime.StageResumeIn(BatchDelay);
    }
}
