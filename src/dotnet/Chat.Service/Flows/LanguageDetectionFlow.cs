using ActualChat.Chat.Module;
using ActualChat.Flows;
using ActualChat.Flows.Infrastructure;
using MemoryPack;

namespace ActualChat.Chat.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class LanguageDetectionFlow : BatchedIndexingFlowBase<ChatEntryLanguage, ChatEntryId>, IMasterFlow
{
    protected override int CurrentFlowSetVersion => 1;
    protected override int BatchSize => 30;

    [field: AllowNull, MaybeNull]
    private IChatEntryLanguagesBackend ChatEntryLanguagesBackend => field ??= Host.Services.GetRequiredService<IChatEntryLanguagesBackend>();
    [field: AllowNull, MaybeNull]
    private IChatsBackend ChatsBackend => field ??= Host.Services.GetRequiredService<IChatsBackend>();
    [field: AllowNull, MaybeNull]
    private Translator Translator => field ??= Host.Services.GetRequiredService<Translator>();
    [field: AllowNull, MaybeNull]
    private ChatSettings Settings => field ??= Host.Services.GetRequiredService<ChatSettings>();
    [field: AllowNull, MaybeNull]
    private Tracer Tracer => field ??= Host.Services.Tracer(GetType());

    protected override async Task<FlowTransition> OnIndex(CancellationToken cancellationToken)
    {
        if (!Settings.IsTranslationEnabled)
        {
            Log.LogInformation("`{Id}`.OnBeforeFirstIndexAfterReset: translation is disabled, flow will not start", Id);
            return WaitForEvent(FlowSteps.OnReset, InfiniteHardResumeAt);
        }

        return await base.OnIndex(cancellationToken).ConfigureAwait(false);
    }

    protected override async Task<IReadOnlyList<ChatEntryLanguage>> GetBatch(
        IndexingFlowCursor<ChatEntryId>? cursor,
        CancellationToken cancellationToken)
    {
        // we do not use cursor in this job
        var batch = await ChatEntryLanguagesBackend.ListForDetection(BatchSize, cancellationToken).ConfigureAwait(false);
        DebugLog?.LogDebug("`{Id}`.GetBatch: retrieved {Count} items with cursor={Cursor}", Id, batch.Count, cursor);
        return batch;
    }

    protected override async Task ProcessBatch(IReadOnlyList<ChatEntryLanguage> batch, CancellationToken cancellationToken)
    {
        using var _1 = Tracer.Region();
        var entries = await batch.Select(x => x.Id)
            .GroupBy(id => id.ChatId)
            .Select(x => ChatsBackend.GetEntries(x, false, cancellationToken).AsTask())
            .Collect(cancellationToken)
            .Flatten()
            .ConfigureAwait(false);
        var languageMap = await Translator
            .DetectLanguages(entries, Settings.BulkLanguageDetectionTimeout, cancellationToken)
            .ConfigureAwait(false);
        await Save().ConfigureAwait(false);
        return;

        async Task Save()
        {
            using var _2 = Tracer.Region();
            var updated = batch.Select(x => x with { Languages = languageMap.GetValueOrDefault(x.Id) });
            var cmd = ChatEntryLanguagesBackend_BulkChange.Upserts(updated);
            var results = await Host.Commander.Call(cmd, cancellationToken).ConfigureAwait(false);
            var failed = results.Zip(batch, (result, entryLanguage) => (result, language: entryLanguage)).Where(x => x.result.HasError).ToList();
            Log.LogInformation("`{Id}`.ProcessBatch: updated {SuccessCount} entryLanguages successfully, {FailedCount} failed", Id, results.Count - failed.Count, failed.Count);
            foreach (var (result, entryLanguage) in failed)
                Log.LogError(result.Error, "`{Id}`.ProcessBatch: failed to update entryLanguage #{Id}", entryLanguage.Id, entryLanguage.Id);
        }
    }
}
