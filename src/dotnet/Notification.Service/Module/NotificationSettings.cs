using ActualLab.IO;

namespace ActualChat.Notification.Module;

public sealed class NotificationSettings
{
    public ApnsSettings Apns { get; set; } = new ();
}

public sealed class ApnsSettings
{
    public string KeyId { get; set; } = "";
    public FilePath KeyPath { get; set; }
}
