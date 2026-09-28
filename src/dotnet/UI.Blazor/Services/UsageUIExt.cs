namespace ActualChat.UI.Blazor.Services;

public static class UsageUIExt
{
    public static void RecordFunnelEvent(this UIHub hub, FunnelEvent funnelEvent)
        // Fire-and-forget: a lost count must never surface in the UI
        => _ = hub.Commander
            .Call(new Usage_RecordFunnelEvent { Session = hub.Session, Event = funnelEvent }, CancellationToken.None)
            .WithErrorLog(hub.Services.LogFor(typeof(UsageUIExt)), "Failed to record funnel event {Event}", funnelEvent);

    public static void RecordOnboardingStep(this UIHub hub, string step, bool isCompleted)
    {
        var log = hub.Services.LogFor(typeof(UsageUIExt));
        if (!OnboardingSteps.IsValid(step)) {
            log.LogWarning("RecordOnboardingStep: unknown onboarding step {Step}", step);
            return;
        }

        var command = new Usage_RecordOnboardingStep { Session = hub.Session, Step = step, IsCompleted = isCompleted };
        _ = hub.Commander.Call(command, CancellationToken.None)
            .WithErrorLog(log, "Failed to record onboarding step {Step}", step);
    }
}
