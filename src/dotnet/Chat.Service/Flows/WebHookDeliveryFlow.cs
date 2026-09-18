using ActualChat.Flows;

namespace ActualChat.Chat.Flows;

/// <summary>
/// Drains one web hook's outbox in order, one flow per hook: it stops at the first delivery
/// that has to wait for a retry, so a receiver never sees an event before the one it follows.
/// </summary>
[Flow(DelayQuanta = 1, ResumeTimeout = 120)]
[DataContract, MessagePackObject(true)]
public sealed partial class WebHookDeliveryFlow : Flow<Unit>
{
    private const int MaxDeliveriesPerResume = 50;
    // A delivery can take the 10 s send timeout plus a RecordDelivery round-trip, and the whole resume
    // has to fit inside the 120 s ResumeTimeout - a resume cut off mid-send loses the attempt's result
    // and re-POSTs the same row under the same webhook-id
    private static readonly TimeSpan ResumeBudget = TimeSpan.FromSeconds(60);

    private WebHookDeliverer Deliverer => field ??= Services.GetRequiredService<WebHookDeliverer>();
    private WebHookId HookId => field ??= WebHookId.Parse(Id.Arguments);

    protected override async ValueTask Resume(CancellationToken cancellationToken)
    {
        var startedAt = CpuTimestamp.Now;
        for (var i = 0; i < MaxDeliveriesPerResume && startedAt.Elapsed < ResumeBudget; i++) {
            var outcome = await Deliverer.DeliverNext(HookId, cancellationToken).ConfigureAwait(false);
            if (!outcome.HasMore)
                return;
            if (outcome.RetryIn is { } delay) {
                Runtime.StageResumeIn(delay);
                return;
            }
        }

        // There is more, but one hook doesn't get to monopolize the runner, and the budget is spent
        Runtime.StageResume();
    }
}
