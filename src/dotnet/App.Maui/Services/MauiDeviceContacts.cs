using ActualChat.Contacts;
using ActualChat.Contacts.UI.Blazor.Services;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;
using banditoth.MAUI.DeviceId.Interfaces;
using MauiContacts = Microsoft.Maui.ApplicationModel.Communication.Contacts;
using MauiContact = Microsoft.Maui.ApplicationModel.Communication.Contact;

namespace ActualChat.App.Maui.Services;

public sealed class MauiDeviceContacts(IServiceProvider services) : DeviceContacts
{
    private Symbol? _deviceId;
    private Session Session { get; } = services.Session();
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IDeviceIdProvider DeviceIdProvider { get; } = services.GetRequiredService<IDeviceIdProvider>();

    public override async Task<ApiArray<ExternalContact>> List(CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(Session, cancellationToken).ConfigureAwait(false);
        var deviceContacts = await MauiContacts.GetAllAsync(cancellationToken).ConfigureAwait(false);
        return deviceContacts.Select(x => ToExternalContact(account.Id, x)).ToApiArray();
    }

    public override Symbol DeviceId => _deviceId ??= DeviceIdProvider.GetDeviceId();

    private ExternalContact ToExternalContact(UserId ownerId, MauiContact mauiContact)
        => new (ExternalContactId.None) {
            Id = new ExternalContactId(ownerId, DeviceId, mauiContact.Id),
            GivenName = mauiContact.GivenName,
            DisplayName = mauiContact.DisplayName,
            FamilyName = mauiContact.FamilyName,
            MiddleName = mauiContact.MiddleName,
            NamePrefix = mauiContact.NamePrefix,
            NameSuffix = mauiContact.NameSuffix,
            Phones = mauiContact.Phones.Select(ToPhone).Where(x => x.IsValid).ToApiSet(),
            Emails = mauiContact.Emails.Select(ToEmail).ToApiSet(StringComparer.OrdinalIgnoreCase),
        };

    private Phone ToPhone(ContactPhone mauiPhone)
        => PhoneFormatterExt.FromReadable(mauiPhone.PhoneNumber);

    private static string ToEmail(ContactEmail mauiEmail)
        => mauiEmail.EmailAddress.ToLowerInvariant();
}
