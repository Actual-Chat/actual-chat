using ActualChat.Contacts;

namespace ActualChat.UI.Blazor.App.Services;

public class DeviceContacts
{
    public virtual Symbol DeviceId => Symbol.Empty;

    public virtual Task<ExternalContactFull[]> List(CancellationToken cancellationToken)
        => Task.FromResult(Array.Empty<ExternalContactFull>());
}
