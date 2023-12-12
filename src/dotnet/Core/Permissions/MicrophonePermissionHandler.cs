namespace ActualChat.Permissions;

public abstract class MicrophonePermissionHandler(Hub hub, bool mustStart = true)
    : PermissionHandler(hub, mustStart);
