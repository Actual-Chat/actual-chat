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

    private WebHookDeliverer Deliverer => field ??= Services.GetRequiredService<WebHookDeliverer>();
    private WebHookId HookId => field ??= WebHookId.Parse(Id.Arguments);

    protected override async ValueTask Resume(CancellationToken cancellationToken)
    {
        for (var i = 0; i < MaxDeliveriesPerResume; i++) {
            var outcome = await Deliverer.DeliverNext(HookId, cancellationToken).ConfigureAwait(false);
            if (!outcome.HasMore)
                return;
            if (outcome.RetryIn is { } delay) {
                Runtime.StageResumeIn(delay);
                return;
            }
        }

        // There is more, but one hook doesn't get to monopolize the runner
        Runtime.StageResume();
    }
}
