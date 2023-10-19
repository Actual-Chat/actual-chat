using ActualChat.Permissions;

namespace ActualChat.Contacts.UI.Blazor;

public class WebContactsPermissionHandler : ContactsPermissionHandler
{
    public WebContactsPermissionHandler(IServiceProvider services, bool mustStart = true) : base(services, mustStart)
        => ExpirationPeriod = null; // We don't need expiration period - there is no Contacts at a web browser

    protected override Task<bool?> Get(CancellationToken cancellationToken)
        => Task.FromResult<bool?>(true);

    protected override Task<bool> Request(CancellationToken cancellationToken)
        => Stl.Async.TaskExt.TrueTask;

    protected override Task Troubleshoot(CancellationToken cancellationToken)
        => Task.CompletedTask;
}
