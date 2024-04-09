using System.Text;
using ActualChat.Hashing;
using ActualChat.Kvas;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.Contacts.UI.Blazor.Services;

public sealed class FakeDeviceContacts(IServiceProvider services) : DeviceContacts
{
    private AccountUI? _accountUI;
    private ExternalContactHasher? _externalContactHasher;

    private AccountUI AccountUI => _accountUI ??= services.GetRequiredService<AccountUI>();
    private ExternalContactHasher ExternalContactHasher => _externalContactHasher ??= services.GetRequiredService<ExternalContactHasher>();

    private IKvas LocalSettings { get; } = services.LocalSettings().WithPrefix<FakeDeviceContacts>();

    public override Symbol DeviceId => "LOCAL";

    public async Task BumpSeed(CancellationToken cancellationToken)
    {
        // If needed all the params can be configured on test page
        var options = await GetOptions(cancellationToken).ConfigureAwait(false) ?? new FakeDeviceContactOptions();
        options = options with {
            Seed = options.Seed + 1,
        };
        await LocalSettings.Set("Options", options, cancellationToken).ConfigureAwait(false);
        // TODO(AY): LocalSettings.Set must wait for writing completion
        await Task.Delay(3000, cancellationToken).ConfigureAwait(false);
    }

    private IEnumerable<ExternalContactFull> GenerateContacts(FakeDeviceContactOptions options, UserId ownerId)
    {
        var phoneCodes = PhoneCodes.List;
        var random = new Random(options.Seed); // no random values)
        var userDeviceId = new UserDeviceId(ownerId, DeviceId);
        return Enumerable.Range(options.ContactStartIndex, options.ContactCount).Select(GenerateExternalContact);

        ExternalContactFull GenerateExternalContact(int contactIndex)
        {
            var externalContactId = new ExternalContactId(userDeviceId, $"contact{contactIndex}");
            var phoneHashes = Enumerable.Range(1, options.PhoneCount)
                .Select(GeneratePhone)
                .Select(x => x.Hash(Encoding.UTF8).SHA256().Base64())
                .ToApiSet();
            var emailHashes = Enumerable.Range(1, options.EmailCount)
                .Select(i => GenerateEmail(contactIndex, i))
                .Select(x => x.Hash(Encoding.UTF8).SHA256().Base64())
                .ToApiSet();
            return new ExternalContactFull(externalContactId) {
                GivenName = $"User {contactIndex}",
                FamilyName = "Generated",
                DisplayName = $"Generated User {contactIndex}",
                PhoneHashes = phoneHashes,
                EmailHashes = emailHashes,
            }.WithHash(ExternalContactHasher);
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

    public override async Task<ApiArray<ExternalContactFull>> List(CancellationToken cancellationToken)
    {
        var cAccount = await AccountUI.OwnAccount.When(x => x.IsActive(), cancellationToken).ConfigureAwait(false);
        var options = await GetOptions(cancellationToken).ConfigureAwait(false);
        return options is not null
            ? GenerateContacts(options, cAccount.Value.Id).ToApiArray()
            : [];
    }

    private async Task<FakeDeviceContactOptions?> GetOptions(CancellationToken cancellationToken)
        => await LocalSettings.Get<FakeDeviceContactOptions>("Options", cancellationToken).ConfigureAwait(false);
}
