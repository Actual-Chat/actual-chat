using System.Diagnostics.CodeAnalysis;
using System.Net.Mail;
using System.Text;
using ActualChat.Contacts;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.Core.NonWasm;
using ActualChat.Hashing;
using ActualChat.Permissions;
using ActualChat.Users;
using banditoth.MAUI.DeviceId.Interfaces;
using MauiContact = Microsoft.Maui.ApplicationModel.Communication.Contact;

namespace ActualChat.App.Maui.Services;

[method: DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MauiContacts))]
public class MauiContacts(IServiceProvider services) : DeviceContacts
{
    private readonly object _lock = new();
    private StrongBox<Symbol>? _deviceId;
    private Session? _session;
    private IAccounts? _accounts;
    private ContactsPermissionHandler? _permissions;
    private ExternalContactHasher? _externalContactHashes;
    private ILogger? _log;

    public override Symbol DeviceId => GetDeviceId();
    private Session Session => _session ??= services.Session();
    private IAccounts Accounts => _accounts ??= services.GetRequiredService<IAccounts>();
    private ContactsPermissionHandler Permissions => _permissions ??= services.GetRequiredService<ContactsPermissionHandler>();
    private ExternalContactHasher ExternalContactHasher => _externalContactHashes ??= services.GetRequiredService<ExternalContactHasher>();
    private IDeviceIdProvider DeviceIdProvider { get; } = services.GetRequiredService<IDeviceIdProvider>();
    private ILogger Log => _log ??= services.LogFor<MauiContacts>();

    public override async Task<ApiArray<ExternalContactFull>> List(CancellationToken cancellationToken)
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

    // Private methods

    private Symbol GetDeviceId()
    {
        // Symbol? size is sizeof((bool, int, pointer)), so the ??= assignment is non-atomic -
        // that's why we use StrongBox to safely access it.

        // Double-check locking
        if (_deviceId == null)
            lock (_lock)
                _deviceId ??= new StrongBox<Symbol>(DeviceIdProvider.GetDeviceId());
        return _deviceId.Value;
    }

    private ExternalContactFull ToExternalContact(
        UserId ownerId,
        PhoneNumberExtractor phoneNumberExtractor,
        MauiContact mauiContact)
        => new ExternalContactFull(new ExternalContactId(new UserDeviceId(ownerId, DeviceId),
            mauiContact.Id.Replace(':', '_'))) {
            GivenName = mauiContact.GivenName ?? "",
            DisplayName = mauiContact.DisplayName ?? "",
            FamilyName = mauiContact.FamilyName ?? "",
            MiddleName = mauiContact.MiddleName ?? "",
            NamePrefix = mauiContact.NamePrefix ?? "",
            NameSuffix = mauiContact.NameSuffix ?? "",
            PhoneHashes = mauiContact.Phones.Select(p => GetPhoneHash(p, phoneNumberExtractor))
                .SkipNullItems()
                .ToApiSet(),
            EmailHashes = mauiContact.Emails.Select(GetEmailHash).SkipNullItems().ToApiSet(),
        }.WithHash(ExternalContactHasher);

    private static string? GetPhoneHash(ContactPhone mauiPhone, PhoneNumberExtractor phoneNumberExtractor)
    {
        var phone = phoneNumberExtractor.GetFromNumber(mauiPhone.PhoneNumber);
        return !phone.IsValid ? null : phone.Hash(Encoding.UTF8).SHA256().Base64();
    }

    private static string? GetEmailHash(ContactEmail mauiEmail)
    {
        if (mauiEmail.EmailAddress.IsNullOrEmpty() || !MailAddress.TryCreate(mauiEmail.EmailAddress, out _))
            return null;

        return mauiEmail.EmailAddress.ToLowerInvariant().Hash(Encoding.UTF8).SHA256().Base64();
    }
}
