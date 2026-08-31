using ActualChat.Kvas;

namespace ActualChat.UI.Blazor.App.Services;

public static class Ptt
{
    public const string IsEnabledOnDeviceKey = "Ptt.IsEnabledOnDevice";

    // For code outside the UI scope (e.g. the MAUI token-refresh path); scoped code
    // reads ChatAudioUI.IsPttEnabledOnDevice instead.
    public static async Task<bool> IsEnabledOnDevice(LocalSettings localSettings, CancellationToken cancellationToken)
    {
        var box = await localSettings.Get<Box<bool>>(IsEnabledOnDeviceKey, cancellationToken).ConfigureAwait(false);
        return box?.Value ?? false;
    }

    public static bool IsSupported(HostInfo hostInfo)
        // Apple's Push to Talk framework needs com.apple.developer.push-to-talk, which
        // Entitlements.prod.plist deliberately doesn't grant until the feature is tested - arming
        // there would fail inside PTChannelManager, and offering an inert control is what App
        // Store review guideline 2.3.1 is about. See App.Maui.csproj's VerifyIosProdCapabilities.
        => hostInfo.AppKind != AppKind.Ios || !hostInfo.IsProductionInstance;

    public static bool IsStaleWake(Moment startedAt, Moment now)
        => now - startedAt > Constants.Audio.PttStaleWakeAge;

    public static Moment GetWakeAnswerStamp(Moment startedAt, Moment now)
        // A fresh wake stamps its handling time, so the answer window runs from when the user is
        // actually alerted and a boot delay can't eat it; a stale wake keeps its original stamp,
        // so it can't arm a hands-free reply long after the fact.
        => IsStaleWake(startedAt, now) ? startedAt : now;

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

    public static bool IsSilenced(DeviceRingerMode mode, bool isDndActive)
        // Any Do Not Disturb counts, including one configured to let priority callers through:
        // whoever set it up didn't consent to a stranger's voice out of the speaker.
        => mode != DeviceRingerMode.Normal || isDndActive;

    public static PttJoinBannerKind GetJoinBannerKind(
        bool isArmedInChat, bool isDeviceEnabled, Moment dismissedAt, Moment enabledAt)
    {
        // The dismissal expires with the chat's enable-epoch: an owner's off-on cycle re-asks.
        if (dismissedAt >= enabledAt)
            return PttJoinBannerKind.None;
        if (!isArmedInChat)
            return PttJoinBannerKind.AllowChat;

        return isDeviceEnabled ? PttJoinBannerKind.None : PttJoinBannerKind.EnableDevice;
    }
}

public enum PttJoinBannerKind
{
    None,
    AllowChat,
    EnableDevice,
}

// Platform-independent view of the phone's alert mode; hosts that can't tell report Normal.
public enum DeviceRingerMode
{
    Normal = 0,
    Vibrate = 1,
    Silent = 2,
}
