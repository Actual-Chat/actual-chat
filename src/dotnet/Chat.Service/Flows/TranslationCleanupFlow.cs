using ActualChat.Chat.Module;
using ActualChat.Flows;
using MemoryPack;

namespace ActualChat.Chat.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class TranslationCleanupFlow : PeriodicFlow, IMasterFlow
{
    private const int BatchSize = 50;
    private ChatSettings Settings => field ??= Services.GetRequiredService<ChatSettings>();
    private ITranslationsBackend TranslationsBackend => field ??= Services.GetRequiredService<ITranslationsBackend>();
    private ICommander Commander => field ??= Services.Commander();

    protected override async Task Run(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested) {
            var translations = await TranslationsBackend.ListHanging(default, BatchSize, cancellationToken).ConfigureAwait(false);
            if (translations.Count == 0)
                return;

            Console.Log($"Finalizing {translations.Count} hanging translations");
            var results = await translations.Select(FinalizeTranslation).CollectResults(cancellationToken).ConfigureAwait(false);
            var failedCount = results.Count(x => x.HasError);
            if (failedCount > 0) {
                Console.LogError($"Failed to finalize {failedCount} of {translations.Count} hanging translations");
                return; // intentional to break error loop
            }
        }
        return;

        Task<Translation> FinalizeTranslation(Translation translation)
        {
            var cmd = new TranslationsBackend_Change(translation.Id,
                translation.Version,
                Change.Remove<TranslationDiff>());
            return Commander
                .Call(cmd, isOutermost: true, cancellationToken)
                .WithErrorLog(cancellationToken, Runtime.Log, "Failed to clean up translation #{Id}", translation.Id);
        }
    }

    protected override ValueTask<Moment> GetNextRunAt(CancellationToken cancellationToken)
        => new(LastRunAt + Settings.Translation.CleanupInterval);
}
