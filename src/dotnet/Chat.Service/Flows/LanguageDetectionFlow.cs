using ActualChat.Chat.Module;
using ActualChat.Flows;
using ActualChat.Flows.Infrastructure;
using ActualLab.Versioning;
using MemoryPack;

namespace ActualChat.Chat.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class LanguageDetectionFlow : BatchedIndexingFlowBase<LanguageDetectionFlow.Item, ChatEntryId>, IMasterFlow
{
    protected override int CurrentFlowSetVersion => 1;
    protected override int BatchSize => Settings.LanguageDetectionFlowBatchSize;
    protected override int Quota => Settings.LanguageDetectionFlowQuota;

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

    protected override async Task<IReadOnlyList<Item>> GetBatch(
        IndexingFlowCursor<ChatEntryId>? cursor,
        CancellationToken cancellationToken)
    {
        var entryLanguages = await ChatEntryLanguagesBackend.ListForDetection(Settings.LanguageDetectionFlowBatchSize, cancellationToken).ConfigureAwait(false);
        var entries = await entryLanguages.Select(x => x.Id)
            .GroupBy(id => id.ChatId)
            .Select(x => ChatsBackend.GetEntries(x, false, cancellationToken).AsTask())
            .Collect(cancellationToken)
            .Flatten()
            .ConfigureAwait(false);
        return entryLanguages.Join(entries, x => x.Id, x => x.Id, Item.From).ToList();
    }

    protected override Task ProcessBatch(IReadOnlyList<Item> batch, CancellationToken cancellationToken)
        => ToChunks(batch)
            .Select(x => DetectLanguages(x, cancellationToken))
            .Collect(Settings.LanguageDetectionParallelDegree, cancellationToken);

    private async Task DetectLanguages(IReadOnlyList<Item> batch, CancellationToken cancellationToken)
    {
        using var _1 = Tracer.Region();
        DebugLog?.LogDebug("DetectLanguages: Detecting for {Count} entries", batch.Count);
        var languageMap = await Detect().ConfigureAwait(false);
        await Save().ConfigureAwait(false);
        return;

        async Task<Dictionary<ChatEntryId, Language[]>> Detect()
        {
            using var _2 = Tracer.Region();
            using var detectionCts = cancellationToken.CreateLinkedTokenSource(Settings.BulkLanguageDetectionTimeout);
            var texts = batch.Select(x => x.Entry.Content.Truncate(Settings.LanguageDetectionEntryContentLimit, "…")).ToList();
            var totalLength = texts.Sum(x => x.Length);
            DebugLog?.LogDebug("`{Id}`.DetectLanguages: Detecting for {Count} texts, total length {TotalLength}", Id, texts.Count, totalLength);
            var sw = Stopwatch.StartNew();
            var languageBulk = await Translator
                .DetectLanguages(texts, detectionCts.Token)
                .WithErrorLog(Log, "Failed to detect languages for {Count} texts with total length {TotalLength}", texts.Count, totalLength)
                .ConfigureAwait(false);
            Log.LogInformation("`{Id}`.DetectLanguages: detected for {Count} texts with total length {TotalLength} in {Elapsed}", Id, texts.Count, totalLength, sw.Elapsed);
            return batch.Zip(languageBulk, (item, languages) => (item, languages))
                .ToDictionary(x => x.item.Entry.Id, x => x.languages);
        }

        async Task Save()
        {
            using var _2 = Tracer.Region();
            var updated = batch.Select(x => x.Language with {
                Languages = languageMap.GetValueOrDefault(x.Entry.Id, []),
            });
            var cmd = ChatEntryLanguagesBackend_BulkChange.Upserts(updated);
            var results = await Host.Commander.Call(cmd, cancellationToken).ConfigureAwait(false);
            // version mismatch is expected, so we don't log it
            var failed = results
                .Zip(batch, (result, entry) => (result, entry))
                .Where(x => x.result.HasError)
                .Where(x => x.result.Error is not VersionMismatchException)
                .ToList();
            Log.LogInformation(
                "`{Id}`.ProcessBatch: updated {SuccessCount} entryLanguages successfully, {FailedCount} failed",
                Id, results.Length - failed.Count, failed.Count);
            foreach (var (result, entry) in failed)
                Log.LogError(result.Error,
                    "`{Id}`.ProcessBatch: failed to update entryLanguage #{Id}",
                    Id, entry.Language.Id);
        }
    }

    private IEnumerable<List<Item>> ToChunks(IReadOnlyList<Item> source)
    {
        var batch = new List<Item>();
        var remainingLength = Settings.LanguageDetectionRequestTokenLimit;
        foreach (var item in source) {
            var contentLength = item.Entry.Content.Length.Clamp(0, Settings.LanguageDetectionEntryContentLimit);
            if (contentLength <= remainingLength) {
                batch.Add(item);
                remainingLength -= contentLength;
            }
            else {
                if (batch.Count > 0)
                    yield return batch;

                batch = [];
                remainingLength = Settings.LanguageDetectionRequestTokenLimit;
            }
        }

        if (batch.Count > 0)
            yield return batch;
    }

    // Helper methods

    public record Item(ChatEntryLanguage Language, ChatEntry Entry) : IHasId<ChatEntryId>, IHasVersion<long>
    {
        public ChatEntryId Id => Language.Id;
        public long Version => Language.Version;

        public static Item From(ChatEntryLanguage language, ChatEntry entry)
            => new (language, entry);
    }
}
