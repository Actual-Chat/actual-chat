using System.Net.Mail;
using ActualChat.Contacts;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using banditoth.MAUI.DeviceId.Interfaces;
using PhoneNumbers;
using MauiContact = Microsoft.Maui.ApplicationModel.Communication.Contact;

namespace ActualChat.App.Maui.Services;

[method: DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MauiContacts))]
public class MauiContacts(IServiceProvider services) : DeviceContacts
{
    private readonly Lock _lock = new();
    private StrongBox<Symbol>? _deviceId;

    public override Symbol DeviceId => GetDeviceId();
    private Session Session => field ??= services.Session();
    private IAccounts Accounts => field ??= services.GetRequiredService<IAccounts>();
    private ContactsPermissionHandler Permissions => field ??= services.GetRequiredService<ContactsPermissionHandler>();
    private ExternalContactHasher ExternalContactHasher => field ??= services.GetRequiredService<ExternalContactHasher>();
    private IDeviceIdProvider DeviceIdProvider { get; } = services.GetRequiredService<IDeviceIdProvider>();
    private ILogger Log => field ??= services.LogFor<MauiContacts>();

    public override async Task<ExternalContactFull[]> List(CancellationToken cancellationToken)
    {
        try {
            var isGranted = await Permissions.Check(cancellationToken).ConfigureAwait(false);
            if (isGranted != true) {
                Log.LogWarning("Contacts permission is isn't granted");
                return [];
            }

            var account = await Accounts.GetOwn(Session, cancellationToken).ConfigureAwait(false);
            var ownPhone = account.IsPhoneVerified()
                ? account.Phone?.E164Value ?? ""
                : "";

            // Get device region as fallback when user has no verified phone
            var deviceRegion = DeviceRegionProvider.GetDeviceRegion();

            // Use user's phone region if available, otherwise use device region
            var phoneParser = !ownPhone.IsNullOrEmpty()
                ? PhoneParser.ForOwnPhone(ownPhone)
                : PhoneParser.ForRegion(deviceRegion);

            var deviceContacts = await GetDeviceContacts(cancellationToken).ConfigureAwait(false);
            return deviceContacts.Select(x => ToExternalContact(account.Id, phoneParser, x)).ToArray();
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to read device contacts");
            return [];
        }
    }

    // Protected methods

    protected virtual async Task<IReadOnlyList<MauiContact>> GetDeviceContacts(CancellationToken cancellationToken)
        => (await Microsoft.Maui.ApplicationModel.Communication.Contacts
            .GetAllAsync(cancellationToken)
            .ConfigureAwait(false)).ToList();

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
        PhoneParser phoneParser,
        MauiContact mauiContact)
        => new ExternalContactFull(ExternalContactId.New(UserDeviceId.New(ownerId, DeviceId),
            mauiContact.Id.Replace(':', '_'))) {
            GivenName = mauiContact.GivenName ?? "",
            DisplayName = mauiContact.DisplayName ?? "",
            FamilyName = mauiContact.FamilyName ?? "",
            MiddleName = mauiContact.MiddleName ?? "",
            NamePrefix = mauiContact.NamePrefix ?? "",
            NameSuffix = mauiContact.NameSuffix ?? "",
            PhoneHashes = mauiContact.Phones.Select(p => GetPhoneHash(p, phoneParser))
                .SkipNullItems()
                .ToApiSet(),
            EmailHashes = mauiContact.Emails.Select(GetEmailHash).SkipNullItems().ToApiSet(),
        }.WithHash(ExternalContactHasher);

    private static string? GetPhoneHash(ContactPhone contactPhone, PhoneParser phoneParser)
        => phoneParser.ParseNullable(contactPhone.PhoneNumber)?.Hash;

    private static string? GetEmailHash(ContactEmail contactEmail)
    {
        if (contactEmail.EmailAddress.IsNullOrEmpty() || !MailAddress.TryCreate(contactEmail.EmailAddress, out _))
            return null;

        return ContactIdExt.Hash(contactEmail.EmailAddress);
    }
}
