using System.Net.Mail;
using ActualChat.Contacts;
using ActualChat.Contacts.UI.Blazor.Services;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;
using banditoth.MAUI.DeviceId.Interfaces;
using Dispatcher = Microsoft.AspNetCore.Components.Dispatcher;
using MauiContacts = Microsoft.Maui.ApplicationModel.Communication.Contacts;
using MauiContact = Microsoft.Maui.ApplicationModel.Communication.Contact;
using MauiPermissions = Microsoft.Maui.ApplicationModel.Permissions;

namespace ActualChat.App.Maui.Services;

public sealed class MauiDeviceContacts(IServiceProvider services) : DeviceContacts
{
    private Symbol? _deviceId;
    private Session Session { get; } = services.Session();
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IDeviceIdProvider DeviceIdProvider { get; } = services.GetRequiredService<IDeviceIdProvider>();
    private Dispatcher Dispatcher { get; } = services.GetRequiredService<Dispatcher>();
    private ILogger Log { get; } = services.LogFor<MauiDeviceContacts>();

    public override async Task<ApiArray<ExternalContact>> List(CancellationToken cancellationToken)
    {
        try {
            var permissionStatus = await RequestPermission().ConfigureAwait(false);
            switch (permissionStatus) {
            case PermissionState.Granted:
                var account = await Accounts.GetOwn(Session, cancellationToken).ConfigureAwait(false);
                var deviceContacts = (await MauiContacts.GetAllAsync(cancellationToken).ConfigureAwait(false)).ToList();
                return deviceContacts.Select(x => ToExternalContact(account.Id, x)).ToApiArray();
            case PermissionState.Denied:
                Log.LogError("Contact permission is missing");
                return ApiArray<ExternalContact>.Empty;
            default:
                Log.LogError("Unexpected contact permission status {Status}", permissionStatus);
                return ApiArray<ExternalContact>.Empty;
            }
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to read contacts from device");
            return ApiArray<ExternalContact>.Empty;
        }
    }

    private async Task<PermissionState> RequestPermission()
    {
        var status = await Dispatcher.InvokeAsync(MauiPermissions.RequestAsync<MauiPermissions.ContactsRead>)
            .ConfigureAwait(false);
        return status switch {
            PermissionStatus.Denied => PermissionState.Denied,
            PermissionStatus.Disabled => PermissionState.Denied,
            PermissionStatus.Restricted => PermissionState.Denied,
            PermissionStatus.Limited => PermissionState.Denied,
            PermissionStatus.Granted => PermissionState.Granted,
            _ => PermissionState.Prompt,
        };
    }

    public override Symbol DeviceId => _deviceId ??= DeviceIdProvider.GetDeviceId();

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
