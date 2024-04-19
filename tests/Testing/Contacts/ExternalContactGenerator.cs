using System.Text;
using ActualChat.Contacts;
using ActualChat.Hashing;

namespace ActualChat.Testing.Contacts;

public class ExternalContactGenerator(int seed = 100)
{
    private readonly Random _random = new (seed);

    public ExternalContactFull NewExternalContact(UserDeviceId userDeviceId = default, int? i = null)
        => new (NewId(userDeviceId)) {
            GivenName = "User",
            FamilyName = (i ?? _random.Next()).ToInvariantString(),
            PhoneHashes = NewPhoneHashes(),
            EmailHashes = NewEmailHashes(),
        };

    public Symbol NewDeviceContactId()
        => _random.Next().ToInvariantString("00000000");

    public ApiSet<string> NewPhoneHashes(int minCount = 0, int maxCount = 10)
        => Enumerable.Range(1, _random.Next(minCount, maxCount)).Select(_ => NewPhoneHash()).ToApiSet();

    public ApiSet<string> NewEmailHashes(int minCount = 0, int maxCount = 10)
        => Enumerable.Range(1, _random.Next(minCount, maxCount)).Select(_ => NewEmailHash()).ToApiSet();

    public string NewPhoneHash()
        => ("1-" + _random.Next().ToInvariantString("0000000000"))
            .Hash(Encoding.UTF8)
            .SHA256()
            .Base64();

    public string NewEmailHash()
        => ("user." + _random.Next().ToInvariantString("0000000000") + "@domain.some")
            .Hash(Encoding.UTF8)
            .SHA256()
            .Base64();

    public UserDeviceId NewUserDeviceId()
    {
        var deviceId = new Symbol(_random.Next().ToInvariantString("000000"));
        var userDeviceId = new UserDeviceId(UserId.New(), deviceId);
        return userDeviceId;
    }

    public ExternalContactId NewId(UserDeviceId id = default)
        => id.IsNone
            ? new (NewUserDeviceId(), NewDeviceContactId())
            : new (id, NewDeviceContactId());
}
