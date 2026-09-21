using System.Diagnostics.Metrics;
using ActualChat.Diagnostics;
using ActualChat.UI.Blazor.App.Components;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Shows <see cref="AppReviewModal"/> when the server has a pending review prompt for the user,
/// i.e. right after a live session they qualified in. Idle on hosts with nothing to rate.
/// </summary>
public class AppReviewPromptUI(AppUIHub hub) : UIWorkerBase<AppUIHub>(hub), IComputeService
{
    public static readonly TimeSpan SettleDelay = TimeSpan.FromSeconds(2);
    private static readonly Counter<long> PromptCounter = AppUIInstruments.Meter.CreateCounter<long>(
        "app_review.prompt", null, "Review prompts shown after a live session, by result");

    private AppReviewUI AppReviewUI => Hub.AppReviewUI;
    private IUsage Usage => Hub.Usage;
    private BackgroundStateTracker BackgroundStateTracker
        => field ??= Services.GetRequiredService<BackgroundStateTracker>();

    [ComputeMethod]
    public virtual async Task<PendingReviewPrompt?> GetShowablePrompt(CancellationToken cancellationToken)
    {
        // Foreground and "no modal open" are device facts, so they stay here; the decision itself is the server's
        var pending = await Usage.GetPendingReviewPrompt(Session, cancellationToken).ConfigureAwait(false);
        if (pending is null)
            return null;
        if (await BackgroundStateTracker.IsBackground.Use(cancellationToken).ConfigureAwait(false))
            return null;

        var activeModals = await ModalUI.ActiveModals.Use(cancellationToken).ConfigureAwait(false);
        return activeModals.Count > 0 ? null : pending;
    }

    // Protected/internal methods

    protected override Task OnRun(CancellationToken cancellationToken)
    {
        if (!AppReviewUI.IsAvailable)
            return Task.CompletedTask;

        return AsyncChain.From(ShowPendingPrompts)
            .Log(LogLevel.Debug, Log)
            .RetryForever(RetryDelaySeq.Exp(0.5, 8), Log)
            .RunIsolated(cancellationToken);
    }

    // Private methods

    private async Task ShowPendingPrompts(CancellationToken cancellationToken)
    {
        var cPrompt = await Computed
            .Capture(() => GetShowablePrompt(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        Moment? shownSince = null;
        while (!cancellationToken.IsCancellationRequested) {
            if (cPrompt.ValueOrDefault is { } prompt && prompt.Since != shownSince) {
                // Let the session UI finish closing before the modal lands on top of it
                await Clocks.CpuClock.Delay(SettleDelay, cancellationToken).ConfigureAwait(false);
                cPrompt = await cPrompt.Update(cancellationToken).ConfigureAwait(false);
                if (cPrompt.ValueOrDefault is { } settled && settled.Since == prompt.Since) {
                    shownSince = prompt.Since;
                    PromptCounter.Add(1, new KeyValuePair<string, object?>("result", "shown"));
                    await ModalUI.Show(new AppReviewModal.Model(RecordOutcome), cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                    PromptCounter.Add(1, new KeyValuePair<string, object?>("result", "suppressed"));
            }

            await cPrompt.WhenInvalidated(cancellationToken).ConfigureAwait(false);
            cPrompt = await cPrompt.Update(cancellationToken).ConfigureAwait(false);
        }
    }

    private void RecordOutcome(ReviewPromptOutcome outcome)
        => _ = Commander
            .Call(new Usage_RecordReviewPrompt { Session = Session, Outcome = outcome }, CancellationToken.None)
            .ContinueWith(t => Log.LogWarning(t.Exception, "Failed to record review prompt outcome {Outcome}", outcome),
                TaskContinuationOptions.OnlyOnFaulted);
}
