namespace ActualChat.Permissions;

public abstract class ContactsPermissionHandler(Hub hub, bool mustStart = true)
    : PermissionHandler(hub, mustStart);
