using ActualChat.Permissions;

namespace ActualChat.Contacts.UI.Blazor;

public class WebContactsPermissionHandler(IServiceProvider services, bool mustStart = true)
    : ContactsPermissionHandler(services, mustStart)
{
    protected override Task<bool?> Get(CancellationToken cancellationToken)
        => Task.FromResult<bool?>(false);

    protected override Task<bool> Request(CancellationToken cancellationToken)
        => Stl.Async.TaskExt.FalseTask;

    protected override Task<bool> Troubleshoot(CancellationToken cancellationToken)
        => Stl.Async.TaskExt.FalseTask;

    public override Task OpenSettings()
        => Task.CompletedTask;
}
