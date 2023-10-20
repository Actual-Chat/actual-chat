namespace ActualChat.Permissions;

public abstract class ContactsPermissionHandler(IServiceProvider services, bool mustStart = true)
    : PermissionHandler(services, mustStart);
