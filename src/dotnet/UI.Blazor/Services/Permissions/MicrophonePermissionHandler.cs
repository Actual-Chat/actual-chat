namespace ActualChat.UI.Blazor.Services;

public abstract class MicrophonePermissionHandler(UIHub hub, bool mustStart = true)
    : PermissionHandler(hub, mustStart);
