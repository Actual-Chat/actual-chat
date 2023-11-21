using System.Diagnostics.CodeAnalysis;
using System.Net.Mail;
using ActualChat.Contacts;
using ActualChat.Contacts.UI.Blazor.Services;
using ActualChat.Permissions;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;
using banditoth.MAUI.DeviceId.Interfaces;
using MauiContacts = Microsoft.Maui.ApplicationModel.Communication.Contacts;
using MauiContact = Microsoft.Maui.ApplicationModel.Communication.Contact;
using MauiPermissions = Microsoft.Maui.ApplicationModel.Permissions;

namespace ActualChat.App.Maui.Services;

[method: DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MauiContacts))]
public class MauiContacts(IServiceProvider services) : DeviceContacts
{
    private Symbol? _deviceId;
    private Session? _session;
    private IAccounts? _accounts;
    private ContactsPermissionHandler? _permissions;
    private ILogger? _log;

    public override Symbol DeviceId => _deviceId ??= DeviceIdProvider.GetDeviceId();
    private Session Session => _session ??= services.Session();
    private IAccounts Accounts => _accounts ??= services.GetRequiredService<IAccounts>();
    private ContactsPermissionHandler Permissions => _permissions ??= services.GetRequiredService<ContactsPermissionHandler>();
    private IDeviceIdProvider DeviceIdProvider { get; } = services.GetRequiredService<IDeviceIdProvider>();
    private ILogger Log => _log ??= services.LogFor<MauiContacts>();

    public override async Task<ApiArray<ExternalContact>> List(CancellationToken cancellationToken)
    {
        try {
            var isGranted = await Permissions.Check(cancellationToken).ConfigureAwait(false);
            if (isGranted != true) {
                Log.LogWarning("Contacts permission is isn't granted");
                return default;
            }

            var account = await Accounts.GetOwn(Session, cancellationToken).ConfigureAwait(false);
            var verifiedPhone = account.HasVerifiedPhone() ? account.Phone.ToInternational() : "";
            var phoneNumberExtractor = PhoneNumberExtractor.CreateFor(verifiedPhone);
            var deviceContacts = (await Microsoft.Maui.ApplicationModel.Communication.Contacts
                .GetAllAsync(cancellationToken)
                .ConfigureAwait(false)).ToList();
            return deviceContacts.Select(x => ToExternalContact(account.Id, phoneNumberExtractor, x)).ToApiArray();
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to read device contacts");
            return default;
        }
    }

    private ExternalContact ToExternalContact(
        UserId ownerId,
        PhoneNumberExtractor phoneNumberExtractor,
        MauiContact mauiContact)
        => new (new ExternalContactId(ownerId, DeviceId, mauiContact.Id.Replace(':', '_'))) {
            GivenName = mauiContact.GivenName ?? "",
            DisplayName = mauiContact.DisplayName ?? "",
            FamilyName = mauiContact.FamilyName ?? "",
            MiddleName = mauiContact.MiddleName ?? "",
            NamePrefix = mauiContact.NamePrefix ?? "",
            NameSuffix = mauiContact.NameSuffix ?? "",
            PhoneHashes = mauiContact.Phones.Select(p => GetPhoneHash(p, phoneNumberExtractor)).SkipNullItems().ToApiSet(),
            EmailHashes = mauiContact.Emails.Select(GetEmailHash).SkipNullItems().ToApiSet(),
        };

    private static string? GetPhoneHash(ContactPhone mauiPhone, PhoneNumberExtractor phoneNumberExtractor)
    {
        var phone = phoneNumberExtractor.GetFromNumber(mauiPhone.PhoneNumber);
        return !phone.IsValid ? null : phone.Value.GetSHA256HashCode();
    }

    private static string? GetEmailHash(ContactEmail mauiEmail)
    {
        if (mauiEmail.EmailAddress.IsNullOrEmpty() || !MailAddress.TryCreate(mauiEmail.EmailAddress, out _))
            return null;

        return mauiEmail.EmailAddress.ToLowerInvariant().GetSHA256HashCode();
    }
}
