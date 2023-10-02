using System.Net.Mail;
using ActualChat.Contacts;
using ActualChat.Contacts.UI.Blazor.Services;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;
using banditoth.MAUI.DeviceId.Interfaces;
using MauiContacts = Microsoft.Maui.ApplicationModel.Communication.Contacts;
using MauiContact = Microsoft.Maui.ApplicationModel.Communication.Contact;
using MauiPermissions = Microsoft.Maui.ApplicationModel.Permissions;

namespace ActualChat.App.Maui.Services;

public class MauiContacts(IServiceProvider services) : DeviceContacts
{
    private Symbol? _deviceId;
    private Session Session { get; } = services.Session();
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IContactPermissions Permissions { get; } = services.GetRequiredService<IContactPermissions>();
    private IDeviceIdProvider DeviceIdProvider { get; } = services.GetRequiredService<IDeviceIdProvider>();
    private ILogger Log { get; } = services.LogFor<MauiContacts>();

    public override Symbol DeviceId => _deviceId ??= DeviceIdProvider.GetDeviceId();

    public override async Task<ApiArray<ExternalContact>> List(CancellationToken cancellationToken)
    {
        try {
            var permissionState = await Permissions.Request().ConfigureAwait(false);
            switch (permissionState) {
            case PermissionState.Granted:
                var account = await Accounts.GetOwn(Session, cancellationToken).ConfigureAwait(false);
                var deviceContacts = (await Microsoft.Maui.ApplicationModel.Communication.Contacts.GetAllAsync(cancellationToken).ConfigureAwait(false)).ToList();
                return deviceContacts.Select(x => ToExternalContact(account.Id, x)).ToApiArray();
            case PermissionState.Denied:
                Log.LogError("Contact permission is missing");
                return default;
            default:
                Log.LogError("Unexpected contact permission status {Status}", permissionState);
                return default;
            }
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to read contacts from device");
            return default;
        }
    }

    private ExternalContact ToExternalContact(UserId ownerId, MauiContact mauiContact)
        => new (ExternalContactId.None) {
            Id = new ExternalContactId(ownerId, DeviceId, mauiContact.Id),
            GivenName = mauiContact.GivenName ?? "",
            DisplayName = mauiContact.DisplayName ?? "",
            FamilyName = mauiContact.FamilyName ?? "",
            MiddleName = mauiContact.MiddleName ?? "",
            NamePrefix = mauiContact.NamePrefix ?? "",
            NameSuffix = mauiContact.NameSuffix ?? "",
            PhoneHashes = mauiContact.Phones.Select(GetPhoneHash).SkipNullItems().ToApiSet(),
            EmailHashes = mauiContact.Emails.Select(GetEmailHash).SkipNullItems().ToApiSet(),
        };

    private static string? GetPhoneHash(ContactPhone mauiPhone)
    {
        var phone = PhoneFormatterExt.FromReadable(mauiPhone.PhoneNumber);
        return !phone.IsValid ? null : phone.Value.GetSHA256HashCode();
    }

    private static string? GetEmailHash(ContactEmail mauiEmail)
    {
        if (mauiEmail.EmailAddress.IsNullOrEmpty() || !MailAddress.TryCreate(mauiEmail.EmailAddress, out _))
            return null;

        return mauiEmail.EmailAddress.ToLowerInvariant().GetSHA256HashCode();
    }
}
