namespace ActualChat.UI.Blazor.Services;

public abstract class CameraPermissionHandler(UIHub hub, bool mustStart = true)
    : PermissionHandler(hub, mustStart);
