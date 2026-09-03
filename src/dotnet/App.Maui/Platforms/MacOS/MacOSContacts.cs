using ActualChat.App.Maui.Services;
using Contacts;
using Foundation;
using MauiContact = Microsoft.Maui.ApplicationModel.Communication.Contact;
using MauiContactEmail = Microsoft.Maui.ApplicationModel.Communication.ContactEmail;
using MauiContactPhone = Microsoft.Maui.ApplicationModel.Communication.ContactPhone;

namespace ActualChat.App.Maui;

/// <summary>
/// <see cref="MauiContacts"/> on CNContactStore: the labs Essentials package implements neither
/// Contacts nor a device id, so both come from here.
/// </summary>
[method: DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MacOSContacts))]
public class MacOSContacts(IServiceProvider services) : MauiContacts(services)
{
    private static readonly NSString[] FetchKeys = [
        CNContactKey.Identifier,
        CNContactKey.NamePrefix,
        CNContactKey.GivenName,
        CNContactKey.MiddleName,
        CNContactKey.FamilyName,
        CNContactKey.NameSuffix,
        CNContactKey.PhoneNumbers,
        CNContactKey.EmailAddresses,
    ];

    public override Symbol DeviceId => MauiPreferences.DeviceId;

    // Protected methods

    protected override Task<IReadOnlyList<MauiContact>> GetDeviceContacts(CancellationToken cancellationToken)
        => Task.Run(() => {
            using var store = new CNContactStore();
            using var request = new CNContactFetchRequest(FetchKeys) { UnifyResults = true };
            var contacts = new List<MauiContact>();
            var isCompleted = store.EnumerateContacts(request, out var error, (CNContact contact, ref bool stop) => {
                contacts.Add(ToMauiContact(contact));
                stop = cancellationToken.IsCancellationRequested;
            });
            cancellationToken.ThrowIfCancellationRequested();
            if (!isCompleted)
                throw error != null
                    ? new NSErrorException(error)
                    : StandardError.External("Could not enumerate contacts.");

            return (IReadOnlyList<MauiContact>)contacts;
        }, cancellationToken);

    // Private methods

    private static MauiContact ToMauiContact(CNContact contact)
        => new (contact.Identifier,
            contact.NamePrefix,
            contact.GivenName,
            contact.MiddleName,
            contact.FamilyName,
            contact.NameSuffix,
            contact.PhoneNumbers.Select(p => new MauiContactPhone(p.Value.StringValue)),
            contact.EmailAddresses.Select(e => new MauiContactEmail(e.Value)));
}
