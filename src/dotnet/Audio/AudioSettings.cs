using System;

namespace ActualChat.Audio;

public sealed class AudioSettings
{
    public string Redis { get; set; } = "";
    public string ServiceName { get; set; } = "actual-chat-app-service";
    public string Namespace { get; set; } = "default";
    public TimeSpan IdleRecordingTimeout { get; set; } = TimeSpan.FromSeconds(3 * 60);
    public TimeSpan IdleRecordingTimeoutBeforeCountdown { get; set; } = TimeSpan.FromSeconds(2.5 * 60);
    public TimeSpan IdleRecordingCheckInterval { get; set; } = TimeSpan.FromSeconds(1);
}
