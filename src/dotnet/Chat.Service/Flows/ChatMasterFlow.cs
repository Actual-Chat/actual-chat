using ActualChat.Flows;

namespace ActualChat.Chat.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public partial class ChatMasterFlow : Flow<Unit>, IMasterFlow
{
    [DataMember(Order = 0), MemoryPackOrder(0)]
    public HashSet<string> AppliedMigrations { get; set; } = new(StringComparer.Ordinal);

    protected override async ValueTask Resume(CancellationToken cancellationToken)
        => await ApplyMigration(StartChatAudioEntryMigrationFlow).ConfigureAwait(false);

    // Private methods

    private async ValueTask ApplyMigration(
        Func<CancellationToken, Task> migration,
        [CallerArgumentExpression(nameof(migration))] string name = "")
    {
        if (AppliedMigrations.Contains(name))
            return;

        await migration.Invoke(Runtime.CancellationToken).ConfigureAwait(false);
        AppliedMigrations.Add(name);
    }

    // Migrations

    private Task StartChatAudioEntryMigrationFlow(CancellationToken cancellationToken)
        => Hub.NewResumeEvent<ChatAudioEntryMigrationFlow>().Schedule(cancellationToken);
}
