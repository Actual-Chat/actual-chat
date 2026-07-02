namespace ActualChat.UI.Blazor.Services;

public abstract class LocationPermissionHandler(UIHub hub, bool mustStart = true)
    : PermissionHandler(hub, mustStart);
