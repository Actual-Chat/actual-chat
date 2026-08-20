namespace ActualChat.UI.Blazor.App.Services;

public static class Ptt
{
    public static bool IsSupported(HostInfo hostInfo)
        // Apple's Push to Talk framework needs com.apple.developer.push-to-talk, which
        // Entitlements.prod.plist deliberately doesn't grant until the feature is tested - arming
        // there would fail inside PTChannelManager, and offering an inert control is what App
        // Store review guideline 2.3.1 is about. See App.Maui.csproj's VerifyIosProdCapabilities.
        => hostInfo.AppKind != AppKind.Ios || !hostInfo.IsProductionInstance;

    public static bool IsStaleWake(Moment startedAt, Moment now)
        => now - startedAt > Constants.Audio.PttStaleWakeAge;

    public static Moment? ComputeIdleDropAt(
        bool hasAnyActivity, Moment? lastActiveAt, Moment idleSince, TimeSpan idleTimeout)
    {
        // Activity is a level (LiveStreamUI.HasActivity), so the caller stamps lastActiveAt on
        // the observed active->idle edge; idleSince clamps a stamp leaked from a prior session.
        if (hasAnyActivity)
            return null;

        return Moment.Max(idleSince, lastActiveAt ?? idleSince) + idleTimeout;
    }
    public static bool MayTransmit(bool isPracticeMode, ChatId? recordingChatId)
        => !isPracticeMode && recordingChatId is null;
}
