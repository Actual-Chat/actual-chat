using ActualChat.Contacts;
using ActualChat.Kvas;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

public sealed class FakeDeviceContacts(IServiceProvider services) : DeviceContacts
{
    private AccountUI AccountUI => field ??= services.GetRequiredService<AccountUI>();
    private ExternalContactHasher ExternalContactHasher => field ??= services.GetRequiredService<ExternalContactHasher>();

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
        var userDeviceId = UserDeviceId.New(ownerId, DeviceId);
        return Enumerable.Range(options.ContactStartIndex, options.ContactCount).Select(GenerateExternalContact);

        ExternalContactFull GenerateExternalContact(int contactIndex)
        {
            var externalContactId = ExternalContactId.New(userDeviceId, $"contact{contactIndex}");
            var phoneHashes = Enumerable.Range(1, options.PhoneCount)
                .Select(GeneratePhone)
                .Select(x => x.Hash)
                .ToApiSet();
            var emailHashes = Enumerable.Range(1, options.EmailCount)
                .Select(i => GenerateEmail(contactIndex, i))
                .Select(ContactIdExt.Hash)
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
            var number = "555" + contactIndex.ToString("00000") + i.ToString("00");
            return Phone.New(code.Code, number);
        }

        string GenerateEmail(int contactIndex, int i)
            => $"user{contactIndex:00000}.email{i}" + "@gmail.com";
    }

    public override async Task<ExternalContactFull[]> List(CancellationToken cancellationToken)
    {
        var cAccount = await AccountUI.OwnAccount.Computed
            .When(x => x.IsActive(), cancellationToken)
            .ConfigureAwait(false);
        var options = await GetOptions(cancellationToken).ConfigureAwait(false);
        return options is not null
            ? GenerateContacts(options, cAccount.Value.Id).ToArray()
            : [];
    }

    private async Task<FakeDeviceContactOptions?> GetOptions(CancellationToken cancellationToken)
        => await LocalSettings.Get<FakeDeviceContactOptions>("Options", cancellationToken).ConfigureAwait(false);
}
