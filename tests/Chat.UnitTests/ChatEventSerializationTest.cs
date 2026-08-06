using ActualChat.Contacts;
using ChatModel = ActualChat.Chat.Chat;

namespace ActualChat.Chat.UnitTests;

public class ChatEventSerializationTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly ChatId TestChatId = ChatId.Parse("the-actual-one");
    private static readonly UserId TestUserId = UserId.New();
    private static readonly PlaceId TestPlaceId = PlaceId.New();

    [Fact]
    public void ChatEntryChangedEvent_Basic()
    {
        var entryId = ChatEntryId.New(TestChatId, 1);
        var authorId = AuthorId.New(TestChatId, 5);
        var entry = new TextEntry(entryId, 1) {
            AuthorId = authorId,
            BeginsAt = new Moment(DateTime.UtcNow),
            Content = "Hello",
        };
        var author = new AuthorFull(TestUserId, authorId, 1) {
            Avatar = new Avatar("avatar-1") { Name = "Test" },
        };
        var evt = new ChatEntryChangedEvent(entry, author, ChangeKind.Create, null);
        var s = evt.PassThroughModernSerializers(Out);
        s.Entry.Id.Should().Be(evt.Entry.Id);
        s.Author.Id.Should().Be(evt.Author.Id);
        s.ChangeKind.Should().Be(evt.ChangeKind);
    }

    [Fact]
    public void ChatChangedEvent_Basic()
    {
        var chat = new ChatModel(TestChatId, 1) { Title = "Test" };
        var evt = new ChatChangedEvent(chat, null, ChangeKind.Create);
        evt.AssertPassesThroughSerializers(
            (deserialized, original) => {
                deserialized.Chat.Id.Should().Be(original.Chat.Id);
                deserialized.ChangeKind.Should().Be(original.ChangeKind);
            }, Out);
    }

    [Fact]
    public void AccountChangedEvent_Basic()
    {
        var account = new AccountFull(TestUserId, 1) {
            Status = AccountStatus.Active,
            Name = "Test",
        };
        var evt = new AccountChangedEvent(account, null, ChangeKind.Create);
        evt.AssertPassesThroughSerializers(
            (deserialized, original) => {
                deserialized.Account.Id.Should().Be(original.Account.Id);
                deserialized.ChangeKind.Should().Be(original.ChangeKind);
            }, Out);
    }

    [Fact]
    public void AuthorUpsertedEvent_Basic()
    {
        var authorId = AuthorId.New(TestChatId, 5);
        var author = new AuthorFull(TestUserId, authorId, 1) {
            Avatar = new Avatar("avatar-1") { Name = "Test" },
        };
        var evt = new AuthorUpsertedEvent(author, null);
        evt.AssertPassesThroughSerializers(
            (deserialized, original) => {
                deserialized.Author.Id.Should().Be(original.Author.Id);
            }, Out);
    }

    [Fact]
    public void AuthorsRemovedEvent_Basic()
    {
        var authorId = AuthorId.New(TestChatId, 5);
        var author = new AuthorFull(TestUserId, authorId, 1) {
            Avatar = new Avatar("avatar-1") { Name = "Test" },
        };
        var evt = new AuthorsRemovedEvent([author]);
        evt.AssertPassesThroughSerializers(
            (deserialized, original) => {
                deserialized.Authors.Length.Should().Be(original.Authors.Length);
                deserialized.Authors[0].Id.Should().Be(original.Authors[0].Id);
            }, Out);
    }

    [Fact]
    public void AvatarChangedEvent_Basic()
    {
        var avatar = new AvatarFull(TestUserId, "avatar-1", 1) { Name = "Test" };
        var evt = new AvatarChangedEvent(avatar, null, ChangeKind.Create);
        evt.AssertPassesThroughSerializers(
            (deserialized, original) => {
                deserialized.Avatar.Id.Should().Be(original.Avatar.Id);
                deserialized.ChangeKind.Should().Be(original.ChangeKind);
            }, Out);
    }

    [Fact]
    public void ContactChangedEvent_Basic()
    {
        var contactId = ContactId.NewAny(TestUserId, TestChatId);
        var contact = new Contact(contactId, 1) {
            Chat = new ChatModel(TestChatId),
        };
        var evt = new ContactChangedEvent(contact, null, ChangeKind.Create);
        evt.AssertPassesThroughSerializers(
            (deserialized, original) => {
                deserialized.Contact.Id.Should().Be(original.Contact.Id);
                deserialized.ChangeKind.Should().Be(original.ChangeKind);
            }, Out);
    }

    [Fact]
    public void ExternalContactNameMayHaveChangedEvent_Basic()
    {
        var evt = new ExternalContactNameMayHaveChangedEvent(TestUserId, ["hash1", "hash2"]);
        evt.AssertPassesThroughSerializers(
            (deserialized, original) => {
                deserialized.OwnerUserId.Should().Be(original.OwnerUserId);
            }, Out);
    }

    [Fact]
    public void NewAccountEvent_Basic()
    {
        var evt = new NewAccountEvent(TestUserId);
        evt.AssertPassesThroughSerializers();
    }

    [Fact]
    public void PlaceChangedEvent_Basic()
    {
        var place = new Place(TestPlaceId, 1) { Title = "Test Place" };
        var evt = new PlaceChangedEvent(place, null, ChangeKind.Create);
        evt.AssertPassesThroughSerializers(
            (deserialized, original) => {
                deserialized.Place.Id.Should().Be(original.Place.Id);
                deserialized.ChangeKind.Should().Be(original.ChangeKind);
            }, Out);
    }

    [Fact]
    public void PlaceMembershipChangedEvent_Basic()
    {
        var evt = new PlaceMembershipChangedEvent(TestUserId, TestPlaceId, false);
        evt.AssertPassesThroughSerializers();
    }

    [Fact]
    public void ReactionChangedEvent_Basic()
    {
        var entryId = ChatEntryId.New(TestChatId, 1);
        var authorId = AuthorId.New(TestChatId, 5);
        var reactionAuthorId = AuthorId.New(TestChatId, 10);
        var entry = new TextEntry(entryId, 1) {
            AuthorId = authorId,
            BeginsAt = new Moment(DateTime.UtcNow),
            Content = "Hello",
        };
        var entryIdText = ChatEntryId.New(TestChatId, 1);
        var reaction = new Reaction {
            Id = "reaction-1",
            AuthorId = reactionAuthorId,
            EntryId = entryIdText,
            Emoji = Emojis.ThumbsUp,
        };
        var author = new AuthorFull(TestUserId, authorId, 1) {
            Avatar = new Avatar("avatar-1") { Name = "Author" },
        };
        var reactionAuthor = new AuthorFull(UserId.New(), reactionAuthorId, 1) {
            Avatar = new Avatar("avatar-2") { Name = "Reactor" },
        };
        var evt = new ReactionChangedEvent(reaction, entry, author, reactionAuthor, ChangeKind.Create);
        var s = evt.PassThroughModernSerializers(Out);
        s.Reaction.Id.Should().Be(evt.Reaction.Id);
        s.ChangeKind.Should().Be(evt.ChangeKind);
    }

    [Fact]
    public void UserSignedOutEvent_Basic()
    {
        var evt = new UserSignedOutEvent(TestUserId, Session.New());
        evt.AssertPassesThroughSerializers();
    }
}
