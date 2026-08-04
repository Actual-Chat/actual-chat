using ActualChat.Chat;
using ActualChat.Contacts;
using ActualChat.Hashing;

namespace ActualChat.Contacts.UnitTests;

public class ContactSerializationTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly Session TestSession = Session.New();
    private static readonly ChatId TestChatId = ChatId.Parse("the-actual-one");
    private static readonly UserId TestUserId = UserId.New();
    private static readonly PlaceId TestPlaceId = PlaceId.New();

    // Model types

    [Fact]
    public void Contact_Basic()
    {
        var contactId = ContactId.NewAny(TestUserId, TestChatId);
        var contact = new Contact(contactId, 1) {
            TouchedAt = new Moment(DateTime.UtcNow),
            IsPinned = true,
            PeerContactName = "Friend",
            State = ContactState.Regular,
            Chat = new Chat.Chat(TestChatId) { Title = "Test Chat" },
        };

        var s = contact.PassThroughSerializers(Out);
        s.Id.Should().Be(contact.Id);
        s.Version.Should().Be(contact.Version);
        s.TouchedAt.Should().Be(contact.TouchedAt);
        s.IsPinned.Should().Be(contact.IsPinned);
        s.PeerContactName.Should().Be(contact.PeerContactName);
        s.State.Should().Be(contact.State);
    }

    [Fact]
    public void ExternalContact_Basic()
    {
        var deviceId = UserDeviceId.New(TestUserId, "device1");
        var externalContactId = ExternalContactId.New(deviceId, "1");
        var contact = new ExternalContact(externalContactId, 1) {
            Hash = new HashString("SHA256 Base16 abc123"),
        };

        var s = contact.PassThroughSerializers(Out);
        s.Id.Should().Be(contact.Id);
        s.Hash.Should().Be(contact.Hash);
    }

    [Fact]
    public void ExternalContactFull_Basic()
    {
        var deviceId = UserDeviceId.New(TestUserId, "device1");
        var externalContactId = ExternalContactId.New(deviceId, "1");
        var contact = new ExternalContactFull(externalContactId, 1) {
            DisplayName = "John Doe",
            GivenName = "John",
            FamilyName = "Doe",
            CreatedAt = new Moment(DateTime.UtcNow),
        };

        var s = contact.PassThroughSerializers(Out);
        s.Id.Should().Be(contact.Id);
        s.DisplayName.Should().Be(contact.DisplayName);
        s.GivenName.Should().Be(contact.GivenName);
        s.FamilyName.Should().Be(contact.FamilyName);
    }

    [Fact]
    public void ExternalContactsHash_Basic()
    {
        var deviceId = UserDeviceId.New(TestUserId, "device1");
        var hash = new ExternalContactsHash(deviceId, 1) {
            Hash = new HashString("SHA256 Base16 abc123"),
            CreatedAt = new Moment(DateTime.UtcNow),
            ModifiedAt = new Moment(DateTime.UtcNow),
        };

        var s = hash.PassThroughSerializers(Out);
        s.Id.Should().Be(hash.Id);
        s.Hash.Should().Be(hash.Hash);
    }

    [Fact]
    public void ThreadContact_Basic()
    {
        var parentChatId = ChatId.Parse("the-actual-one");
        var threadChatId = ThreadChatId.Parse(parentChatId.Value + "-1");
        var contactId = ContactId.NewAny(TestUserId, threadChatId);
        var contact = new ThreadContact(contactId, 1) {
            TouchedAt = new Moment(DateTime.UtcNow),
            IsPinned = false,
        };

        var s = contact.PassThroughSerializers(Out);
        s.Id.Should().Be(contact.Id);
        s.TouchedAt.Should().Be(contact.TouchedAt);
        s.IsPinned.Should().Be(contact.IsPinned);
    }

    // API Commands

    [Fact]
    public void Contacts_Touch_Basic()
    {
        var contactId = ContactId.NewAny(TestUserId, TestChatId);
        var cmd = new Contacts_Touch(TestSession, contactId);
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void Contacts_Change_Basic()
    {
        var contactId = ContactId.NewAny(TestUserId, TestChatId);
        var contact = new Contact(contactId, 1) {
            Chat = new Chat.Chat(TestChatId),
        };
        var cmd = new Contacts_Change(TestSession, contactId, null, Change.Update(contact));
        cmd.AssertPassesThroughSerializers(
            (deserialized, original) => {
                deserialized.Id.Should().Be(original.Id);
                deserialized.Session.Should().Be(original.Session);
            }, Out);
    }

    [Fact]
    public void ExternalContactHashes_Change_Basic()
    {
        var deviceId = UserDeviceId.New(TestUserId, "device1");
        var hash = new ExternalContactsHash(deviceId, 1) {
            Hash = new HashString("SHA256 Base16 abc123"),
        };
        var cmd = new ExternalContactHashes_Change(TestSession, deviceId.Id, null, Change.Create(hash));
        cmd.AssertPassesThroughSerializers(
            (deserialized, original) => {
                deserialized.DeviceId.Should().Be(original.DeviceId);
            }, Out);
    }

    // Backend Commands

    [Fact]
    public void ContactsBackend_Change_Basic()
    {
        var contactId = ContactId.NewAny(TestUserId, TestChatId);
        var contact = new Contact(contactId, 1) {
            Chat = new Chat.Chat(TestChatId),
        };
        var cmd = new ContactsBackend_Change(contactId, null, Change.Create(contact));
        cmd.AssertPassesThroughSerializers(
            (deserialized, original) => {
                deserialized.Id.Should().Be(original.Id);
            }, Out);
    }

    [Fact]
    public void ContactsBackend_Touch_Basic()
    {
        var contactId = ContactId.NewAny(TestUserId, TestChatId);
        var cmd = new ContactsBackend_Touch(contactId);
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void ContactsBackend_RemoveAccount_Basic()
    {
        var cmd = new ContactsBackend_RemoveAccount(TestUserId);
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void ContactsBackend_RemoveChatContacts_Basic()
    {
        var cmd = new ContactsBackend_RemoveChatContacts(TestChatId);
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void ContactsBackend_Greet_Basic()
    {
        var cmd = new ContactsBackend_Greet(TestUserId);
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void ContactsBackend_ChangePlaceMembership_Basic()
    {
        var cmd = new ContactsBackend_ChangePlaceMembership(TestPlaceId, TestUserId, false);
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void ContactsBackend_PublishCopiedChat_Basic()
    {
        var placeChatId = PlaceChatId.New(TestPlaceId);
        var cmd = new ContactsBackend_PublishCopiedChat(placeChatId);
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void ContactsBackend_ReviewExternalContactName_Basic()
    {
        var contactId = ContactId.NewAny(TestUserId, TestChatId);
        var cmd = new ContactsBackend_ReviewExternalContactName(contactId);
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void ExternalContactsBackend_RemoveAccount_Basic()
    {
        var cmd = new ExternalContactsBackend_RemoveAccount(TestUserId);
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void ExternalContactHashesBackend_RemoveAccount_Basic()
    {
        var cmd = new ExternalContactHashesBackend_RemoveAccount(TestUserId);
        cmd.AssertPassesThroughSerializers();
    }

    // Query types

    [Fact]
    public void ChangedContactsQuery_Basic()
    {
        var query = new ChangedContactsQuery { MinVersion = 1, Limit = 100, LastId = null };
        query.AssertPassesThroughSerializers();
    }
}
