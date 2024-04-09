namespace ActualChat.Contacts.UI.Blazor.Services;

public class DeviceContacts
{
    public virtual Symbol DeviceId => Symbol.Empty;

    public virtual Task<ApiArray<ExternalContactFull>> List(CancellationToken cancellationToken)
        => Task.FromResult(ApiArray<ExternalContactFull>.Empty);
}
