using ActualChat.Chat.Module;
using ActualChat.Flows;
using ActualChat.Sharding;
using MemoryPack;

namespace ActualChat.Chat.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class TranslationCleanupFlow : PeriodicFlow, IMasterFlow
{
    private const int BatchSize = 50;
    [field: AllowNull, MaybeNull]
    private ChatSettings Settings => field ??= Host.Services.GetRequiredService<ChatSettings>();
    [field: AllowNull, MaybeNull]
    private ITranslationsBackend TranslationsBackend => field ??= Host.Services.GetRequiredService<ITranslationsBackend>();
    [field: AllowNull, MaybeNull]
    private ICommander Commander => field ??= Host.Services.Commander();

    protected override Task<string?> Update(CancellationToken cancellationToken)
        => Task.FromResult<string?>(null);

    protected override async Task Run(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested) {
            var translations = await TranslationsBackend.ListHanging(default, BatchSize, cancellationToken).ConfigureAwait(false);
            if (translations.Count == 0)
                return;

            Log.LogInformation("Finalizing {Count} hanging translations", translations.Count);
            var results = await translations.Select(FinalizeTranslation).CollectResults(cancellationToken).ConfigureAwait(false);
            var failedCount = results.Count(x => x.HasError);
            if (failedCount > 0) {
                Log.LogError("Failed to finalize {FailedCount} of {Count} hanging translations", failedCount, translations.Count);
                return; // intentional to break error loop
            }
        }
        return;

        Task<Translation> FinalizeTranslation(Translation translation)
            => Commander.Call(new TranslationsBackend_Change(translation.Id,
                        translation.Version,
                        Change.Remove<TranslationDiff>()),
                    true,
                    cancellationToken)
                .WithErrorLog(Log, "Failed to clean up translation #{Id}", translation.Id);
    }

    protected override Moment ComputeNextRunAt(Moment now, CancellationToken cancellationToken)
        => now + Settings.Translation.CleanupInterval;
}
