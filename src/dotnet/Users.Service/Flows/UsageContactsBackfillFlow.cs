using ActualChat.Contacts;
using ActualChat.Flows;
using ActualChat.Users.Db;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Users.Flows;

/// <summary>
/// Seeds one <see cref="UsageEventKind.Contact"/> event per existing friend, so the per-day history
/// starts from the current contact lists rather than from zero.
/// </summary>
[Flow(DataVersion = 1, DelayQuanta = 0)]
[DataContract, MessagePackObject(true)]
public sealed partial class UsageContactsBackfillFlow : Flow<(Moment, long)>
{
    private const int BatchSize = 50;
    private static readonly RandomTimeSpan BatchDelay = TimeSpan.FromSeconds(1).ToRandom(0.25);

    private DbHub<UsersDbContext> DbHub => field ??= Services.DbHub<UsersDbContext>();
    private IContactsBackend ContactsBackend => field ??= Services.GetRequiredService<IContactsBackend>();
    private ICommander Commander => Hub.Commander;

    [DataMember(Order = 0), Key(0)]
    public string? LastProcessedId { get; set; }
    [DataMember(Order = 1), Key(1)]
    public long EventCount { get; set; }
    [DataMember(Order = 2), Key(2)]
    public long ScannedCount { get; set; }

    protected override async ValueTask Resume(CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var query = dbContext.Accounts.AsQueryable();
        if (!LastProcessedId.IsNullOrEmpty())
            query = query.Where(x => string.Compare(x.Id, LastProcessedId) > 0);

        var batch = await query
            .OrderBy(x => x.Id)
            .Take(BatchSize)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (batch.Count == 0) {
            Console.Log($"Completed: {EventCount} friends across {ScannedCount} accounts");
            SetResult((Hub.Clocks.SystemClock.Now, EventCount));
            return;
        }

        var backfillDay = UsageDay.DayOf(Hub.Clocks.SystemClock.Now);
        foreach (var sUserId in batch) {
            if (!UserId.TryParse(sUserId, out var userId) || userId.IsGuest)
                continue;

            var events = new List<UsageEvent>();
            var contactIds = await ContactsBackend
                .ListPeerContactIds(userId, null, cancellationToken)
                .ConfigureAwait(false);
            foreach (var contactId in contactIds) {
                if (contactId.Kind != ContactKind.User)
                    continue;

                var contact = await ContactsBackend.Get(userId, contactId, cancellationToken).ConfigureAwait(false);
                if (!UsageEventSource.IsFriend(contact))
                    continue;

                var at = contact.TouchedAt == default ? backfillDay : contact.TouchedAt;
                events.Add(new UsageEvent(UsageEventKind.Contact, at, $"{contact.Id}:{contact.Version}", 1));
            }
            if (events.Count == 0)
                continue;

            await Commander
                .Call(new UsageBackend_Record(userId, events.ToApiArray()), cancellationToken)
                .ConfigureAwait(false);
            EventCount += events.Count;
        }

        LastProcessedId = batch[^1];
        ScannedCount += batch.Count;
        Console.Log($"Progress: {EventCount} friends across {ScannedCount} accounts");
        Runtime.StageResumeIn(BatchDelay.Next());
    }
}
