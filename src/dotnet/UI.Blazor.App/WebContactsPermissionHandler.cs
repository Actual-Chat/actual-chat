using ActualChat.Permissions;

namespace ActualChat.UI.Blazor.App;

public class WebContactsPermissionHandler : ContactsPermissionHandler
{
    public WebContactsPermissionHandler(UIHub hub, bool mustStart = true)
        : base(hub, false)
    {
        // We don't need expiration period - no contacts on Web
        ExpirationPeriod = null;
        if (mustStart)
            this.Start();
    }

    protected override Task<bool?> Get(CancellationToken cancellationToken)
        => Task.FromResult<bool?>(true);

    protected override Task<bool> Request(CancellationToken cancellationToken)
        => ActualLab.Async.TaskExt.TrueTask;

    protected override Task Troubleshoot(CancellationToken cancellationToken)
        => Task.CompletedTask;
}
