using ActualChat.Chat.Module;
using ActualChat.Flows;
using ActualChat.Flows.Infrastructure;
using MemoryPack;

namespace ActualChat.Chat.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class LanguageDetectionFlow : BatchedIndexingFlowBase<ChatEntry, ChatEntryId>
{
    protected override int CurrentFlowSetVersion => 1;
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

    protected override async Task<IReadOnlyList<ChatEntry>> GetBatch(
        IndexingFlowCursor<ChatEntryId>? cursor,
        CancellationToken cancellationToken)
    {
        var chatId = new ChatId(Id.Arguments);
        // we do not use cursor in this job
        var batch = await ChatsBackend.ListEntriesForLanguageDetection(chatId, BatchSize, cancellationToken).ConfigureAwait(false);
        DebugLog?.LogDebug("`{Id}`.GetBatch: retrieved {Count} items with cursor={Cursor}", Id, batch.Count, cursor);
        return batch;
    }

    protected override async Task ProcessBatch(IReadOnlyList<ChatEntry> batch, CancellationToken cancellationToken)
    {
        using var _1 = Tracer.Region();
        var texts = batch.Select(x => x.Content).ToList();
        using var detectionCts = cancellationToken.CreateLinkedTokenSource(Settings.BulkLanguageDetectionTimeout);
        var languageBulk = await Translator.DetectLanguages(texts, detectionCts.Token).ConfigureAwait(false);
        await Save().ConfigureAwait(false);
        return;

        async Task Save()
        {
            using var _2 = Tracer.Region();
            for (var i = 0; i < batch.Count; i++)
            {
                var entry = batch[i];
                var languages = languageBulk[i];
                var change = Change.Update(new ChatEntryDiff { Languages = languages });
                var cmd = new ChatsBackend_ChangeEntry(entry.Id, entry.Version, change);
                await Host.Commander.Call(cmd, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
