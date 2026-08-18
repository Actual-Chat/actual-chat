namespace ActualChat.Notifications.Module;

public sealed class NotificationsSettings
{
    public bool EnablePttPush { get; set; } = true;
    public TimeSpan PttWakeTtl { get; set; } = TimeSpan.FromSeconds(30);
    public int PttMaxChatMembers { get; set; } = 100;
    // Direct-APNs auth for Push to Talk wakes (FCM can't deliver pushtotalk pushes).
    // The .p8 must be an APNs-enabled key - the Apple Sign-In key won't work.
    public string ApplePushKeyId { get; set; } = "";
    public string ApplePushTeamId { get; set; } = "";
    public string ApplePushBundleId { get; set; } = "";
    public string ApplePushPrivateKeyPath { get; set; } = "";
    public bool ApplePushUseSandbox { get; set; }
}
