using ActualChat.Chat.Module;
using ActualChat.Flows;

namespace ActualChat.Chat.Flows;

[Flow(DelayQuanta = 60)]
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public partial class TranslationCleanupFlow : PeriodicFlow, IMasterFlow
{
    private const int BatchSize = 50;

    private ChatSettings Settings => field ??= Services.GetRequiredService<ChatSettings>();
    private ITranslationsBackend TranslationsBackend => field ??= Services.GetRequiredService<ITranslationsBackend>();
    private ICommander Commander => field ??= Services.Commander();

    protected override async ValueTask<FlowReadiness> Prepare(CancellationToken cancellationToken)
    {
        var translations = await TranslationsBackend.ListHanging(default, BatchSize, cancellationToken).ConfigureAwait(false);
        return translations.Count == 0
            ? "No hanging translations to finalize"
            : FlowReadiness.Ready;
    }

    protected override async ValueTask<Moment> Run(CancellationToken cancellationToken)
    {
        var translations = await TranslationsBackend.ListHanging(default, BatchSize, cancellationToken).ConfigureAwait(false);
        if (translations.Count == 0)
            return Moment.MaxValue; // no more work to do - We will wait for resume event.

        Console.Log($"Finalizing {translations.Count} hanging translations");
        var results = await translations.Select(FinalizeTranslation).CollectResults(cancellationToken).ConfigureAwait(false);
        var failedCount = results.Count(x => x.HasError);
        if (failedCount > 0) {
            Console.LogError($"Failed to finalize {failedCount} of {translations.Count} hanging translations");
            return Hub.SystemNow + Settings.Translation.CleanupInterval; // schedule next run anyway, but not immediately to avoid error loop
        }

        return translations.Count == BatchSize
            ? Hub.SystemNow
            : Hub.SystemNow + Settings.Translation.CleanupInterval;

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
}
