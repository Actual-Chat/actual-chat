namespace ActualChat.Notifications.Module;

public sealed class NotificationsSettings
{
    public bool EnablePttPush { get; set; } = true;
    public TimeSpan PttWakeTtl { get; set; } = TimeSpan.FromSeconds(30);
    public int PttMaxChatMembers { get; set; } = 100;
    // An entry removed within UrgentRemovalAge of being posted is cleaned up from its chat members'
    // notifications at once; older removals are batched into one pass per RemovalCleanupDelay.
    public TimeSpan UrgentRemovalAge { get; set; } = TimeSpan.FromHours(1);
    public TimeSpan RemovalCleanupDelay { get; set; } = TimeSpan.FromMinutes(10);
    public int RemovalCleanupBatchSize { get; set; } = 50;
    public TimeSpan RemovalCleanupBatchPeriod { get; set; } = TimeSpan.FromSeconds(1);
    // A user with active notifications is re-checked this often even when nothing triggers it.
    public TimeSpan ConvergePeriod { get; set; } = TimeSpan.FromDays(1);
    // Direct-APNs auth for Push to Talk wakes (FCM can't deliver pushtotalk pushes).
    // The .p8 must be an APNs-enabled key - the Apple Sign-In key won't work.
    public string ApplePushKeyId { get; set; } = "";
    public string ApplePushTeamId { get; set; } = "";
    public string ApplePushBundleId { get; set; } = "";
    public string ApplePushPrivateKeyPath { get; set; } = "";
    public bool ApplePushUseSandbox { get; set; }
}
