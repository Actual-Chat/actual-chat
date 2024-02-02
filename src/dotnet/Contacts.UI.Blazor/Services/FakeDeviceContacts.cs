using System.Text;
using ActualChat.Hashing;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;
using ActualLab.Generators;

namespace ActualChat.Contacts.UI.Blazor.Services;

public class FakeDeviceContacts(IServiceProvider services) : DeviceContacts
{
    public static bool AllowGeneratedContacts => false; // set true for debug purpose on local dev machine
    private ApiArray<ExternalContact> _generated;

    private AccountUI? _accountUI;

    private AccountUI AccountUI => _accountUI ??= services.GetRequiredService<AccountUI>();

    public override Symbol DeviceId => "LOCAL";

    public async Task Regenerate(CancellationToken cancellationToken)
    {
        var cAccount = await AccountUI.OwnAccount.When(x => x.IsActive(), cancellationToken).ConfigureAwait(false);
        _generated = GenerateContacts(cAccount.Value.Id).ToApiArray();
    }

    private IEnumerable<ExternalContact> GenerateContacts(UserId ownerId)
    {
        var phoneCodes = PhoneCodes.List;
        var random = new Random(100); // no random values)

        return Enumerable.Range(1, 10_000).Select(GenerateExternalContact);

        ExternalContact GenerateExternalContact(int contactIndex)
        {
            var externalContactId = new ExternalContactId(ownerId, DeviceId, RandomSymbolGenerator.Default.Next(8));
            var phoneHashes = Enumerable.Range(1, 10)
                .Select(GeneratePhone)
                .Select(x => x.Hash(Encoding.UTF8).SHA256().Base64())
                .ToApiSet();
            var emailHashes = Enumerable.Range(1, 10)
                .Select(i => GenerateEmail(contactIndex, i))
                .Select(x => x.Hash(Encoding.UTF8).SHA256().Base64())
                .ToApiSet();
            return new (externalContactId) {
                GivenName = $"User {contactIndex}",
                FamilyName = "Generated",
                DisplayName = $"Generated User {contactIndex}",
                PhoneHashes = phoneHashes,
                EmailHashes = emailHashes,
            };
        }

        Phone GeneratePhone(int contactIndex, int i)
        {
            var code = phoneCodes[random.Next(0, phoneCodes.Count)];
            var number = "555" + contactIndex.ToString("00000", CultureInfo.InvariantCulture) + i.ToString("00", CultureInfo.InvariantCulture);
            return new Phone(code.Code, number);
        }

        string GenerateEmail(int contactIndex, int i)
            => $"user{contactIndex:00000}.email{i}" + "@gmail.com";
    }

    public override async Task<ApiArray<ExternalContact>> List(CancellationToken cancellationToken)
    {
        if (_generated.IsEmpty)
            await Regenerate(cancellationToken).ConfigureAwait(false);

        return _generated;
    }
}
