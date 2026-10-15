using ActualChat.Flows;

namespace ActualChat.Contacts.Flows;

/// <summary>
/// Moves the contacts blocked in the contact list into the archive, as blocked archived contacts.
/// Retire it into a no-op, keeping the type for the persisted flow state, once it has run everywhere.
/// </summary>
[Flow(DataVersion = 1, DelayQuanta = 0)]
[DataContract, MessagePackObject(true)]
public sealed partial class BlockedContactsMigrationFlow : Flow<(Moment, long)>
{
    private const int BatchSize = 50;
    private static readonly RandomTimeSpan BatchDelay = TimeSpan.FromSeconds(1).ToRandom(0.25);

    private IContactsBackend ContactsBackend => field ??= Services.GetRequiredService<IContactsBackend>();
    private ICommander Commander => Hub.Commander;

    [DataMember(Order = 0), Key(0)]
    public string? LastProcessedId { get; set; }
    [DataMember(Order = 1), Key(1)]
    public long MigratedCount { get; set; }

    protected override async ValueTask Resume(CancellationToken cancellationToken)
    {
        var batch = await ContactsBackend
            .ListBlockedForMigration(LastProcessedId, BatchSize, cancellationToken)
            .ConfigureAwait(false);
        if (batch.Length == 0) {
            Console.Log($"Completed: {MigratedCount} blocked contacts");
            SetResult((Hub.Clocks.SystemClock.Now, MigratedCount));
            return;
        }

        foreach (var contact in batch) {
            var migrateCmd = new ContactsBackend_MigrateBlocked(contact.Id);
            await Commander.Call(migrateCmd, true, cancellationToken).ConfigureAwait(false);
        }

        LastProcessedId = batch[^1].Id.Value;
        MigratedCount += batch.Length;
        Console.Log($"Progress: {MigratedCount} blocked contacts");
        Runtime.StageResumeIn(BatchDelay.Next());
    }
}
