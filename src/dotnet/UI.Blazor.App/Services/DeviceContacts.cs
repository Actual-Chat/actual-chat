using ActualChat.Contacts;

namespace ActualChat.UI.Blazor.App.Services;

public class DeviceContacts
{
    public virtual Symbol DeviceId => Symbol.Empty;

    public virtual Task<ApiArray<ExternalContactFull>> List(CancellationToken cancellationToken)
        => Task.FromResult(ApiArray<ExternalContactFull>.Empty);
}
