using ActualChat.Permissions;

namespace ActualChat.Contacts.UI.Blazor;

public class WebContactsPermissionHandler(IServiceProvider services, bool mustStart = true)
    : ContactsPermissionHandler(services, mustStart)
{
    protected override Task<bool?> Get(CancellationToken cancellationToken)
        => Task.FromResult<bool?>(true);

    protected override Task<bool> Request(CancellationToken cancellationToken)
        => Stl.Async.TaskExt.TrueTask;

    protected override Task<bool> Troubleshoot(CancellationToken cancellationToken)
        => Stl.Async.TaskExt.FalseTask;

    public override Task OpenSettings()
        => Task.CompletedTask;
}
