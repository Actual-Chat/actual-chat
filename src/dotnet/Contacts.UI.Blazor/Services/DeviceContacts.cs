namespace ActualChat.Contacts.UI.Blazor.Services;

public class DeviceContacts
{
    public virtual Symbol DeviceId => Symbol.Empty;

    public virtual Task<ApiArray<ExternalContact>> List(CancellationToken cancellationToken)
        => Task.FromResult(ApiArray<ExternalContact>.Empty);
}
