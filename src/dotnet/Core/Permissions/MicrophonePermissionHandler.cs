namespace ActualChat.Permissions;

public abstract class MicrophonePermissionHandler(IServiceProvider services, bool mustStart = true)
    : PermissionHandler(services, mustStart);
