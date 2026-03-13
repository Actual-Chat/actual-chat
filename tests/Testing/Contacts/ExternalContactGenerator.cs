using System.Text;
using ActualChat.Contacts;
using ActualChat.Hashing;

namespace ActualChat.Testing.Contacts;

public class ExternalContactGenerator(int seed = 100)
{
    private readonly Random _random = new (seed);

    public ExternalContactFull NewExternalContact(UserDeviceId? userDeviceId = null, int? i = null)
        => new (NewId(userDeviceId)) {
            GivenName = "User",
            FamilyName = (i ?? _random.Next()).ToString(),
            PhoneHashes = NewPhoneHashes(),
            EmailHashes = NewEmailHashes(),
        };

    public Symbol NewDeviceContactId()
        => _random.Next().ToString("00000000", null);

    public ApiSet<string> NewPhoneHashes(int minCount = 0, int maxCount = 10)
        => Enumerable.Range(1, _random.Next(minCount, maxCount)).Select(_ => NewPhoneHash()).ToApiSet();

    public ApiSet<string> NewEmailHashes(int minCount = 0, int maxCount = 10)
        => Enumerable.Range(1, _random.Next(minCount, maxCount)).Select(_ => NewEmailHash()).ToApiSet();

    public string NewPhoneHash()
        => ("1-" + _random.Next().ToString("0000000000", null))
            .Hash(Encoding.UTF8)
            .SHA256()
            .Base64();

    public string NewEmailHash()
        => ("user." + _random.Next().ToString("0000000000", null) + "@domain.some")
            .Hash(Encoding.UTF8)
            .SHA256()
            .Base64();

    public UserDeviceId NewUserDeviceId()
    {
        var deviceId = _random.Next().ToString("000000", null);
        return UserDeviceId.New(UserId.New(), deviceId);
    }

    public ExternalContactId NewId(UserDeviceId? id = null)
        => id is null
            ? ExternalContactId.New(NewUserDeviceId(), NewDeviceContactId())
            : ExternalContactId.New(id, NewDeviceContactId());
}
