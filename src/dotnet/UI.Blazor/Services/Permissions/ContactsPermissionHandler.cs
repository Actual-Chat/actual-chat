namespace ActualChat.UI.Blazor.Services;

public abstract class ContactsPermissionHandler(UIHub hub, bool mustStart = true)
    : PermissionHandler(hub, mustStart);
