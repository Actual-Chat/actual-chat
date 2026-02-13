using ActualChat.Contacts;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.Users;
using ActualLab.Mathematics;
using ChatModel = ActualChat.Chat.Chat;

namespace ActualChat.Chat.IntegrationTests;

public class ApiEvolutionTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly bool MustSerialize = false;

    private static readonly ChatId TestChatId = ChatId.Parse("the-actual-one");
    private static readonly UserId TestUserId = UserId.Parse("test-user-1");
    private static readonly AuthorId TestAuthorId = AuthorId.New(TestChatId, 1);
    private static readonly PlaceId TestPlaceId = PlaceId.New();
    private static readonly ChatEntryId TestEntryId = ChatEntryId.New(TestChatId, ChatEntryKind.Text, 1);
    private static readonly Moment TestMoment = new(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));

    private IByteSerializer Serializer => MemoryPackByteSerializer.Default;

    [Fact]
    public void RunTest()
    {
        if (MustSerialize)
            Serialize();
        Deserialize();
    }

    // Private methods

    private void Serialize([CallerFilePath] string? callerFilePath = null)
    {
        var dir = Path.Combine(Path.GetDirectoryName(callerFilePath)!, "data", "api-evolution");
        Directory.CreateDirectory(dir);

        SerializeOne(CreateAccountFull(), dir);
        SerializeOne(CreateSessionInfo(), dir);
        SerializeOne(CreateChat(), dir);
        SerializeOne(CreateAuthorRules(), dir);
        SerializeOne(CreateChatNews(), dir);
        SerializeOne(CreateChatEntry(), dir);
        SerializeOne(CreateChatTile(), dir);
        SerializeOne(CreateAuthorFull(), dir);
        SerializeOne(CreateContact(), dir);
        SerializeOne(CreatePlace(), dir);
        SerializeOne(CreatePlaceRules(), dir);
        SerializeOne(CreateChatPosition(), dir);
        SerializeOne(CreateMention(), dir);
        SerializeOne(CreateUserLanguageSettings(), dir);
        SerializeOne(CreateUserOnboardingSettings(), dir);
        SerializeOne(CreateUserBubbleSettings(), dir);
        SerializeOne(CreateChatListSettings(), dir);
        SerializeOne(CreateActiveChat(), dir);
        SerializeOne(CreateUserPresencesCheckIn(), dir);
    }

    private void Deserialize([CallerFilePath] string? callerFilePath = null)
    {
        var dir = Path.Combine(Path.GetDirectoryName(callerFilePath)!, "data", "api-evolution");

        DeserializeOne<AccountFull>(dir, v => {
            v.Name.Should().Be("Test User");
            v.Email.Should().Be("test@example.com");
        });
        DeserializeOne<SessionInfo>(dir, v => {
            v.IPAddress.Should().Be("127.0.0.1");
        });
        DeserializeOne<ChatModel>(dir, v => {
            v.Title.Should().Be("Test Chat");
            v.IsPublic.Should().BeTrue();
        });
        DeserializeOne<AuthorRules>(dir, v => {
            v.CanRead().Should().BeTrue();
            v.CanWrite().Should().BeTrue();
        });
        DeserializeOne<ChatNews>(dir, v => {
            v.TextEntryIdRange.Should().Be(new Range<long>(1, 100));
            v.LastTextEntry.Should().NotBeNull();
        });
        DeserializeOne<ChatEntry>(dir, v => {
            v.Content.Should().Be("Hello, world!");
            v.AuthorId.Should().Be(TestAuthorId);
        });
        DeserializeOne<ChatTile>(dir, v => {
            v.Entries.Length.Should().Be(1);
            v.IdTileRange.Should().Be(new Range<long>(0, 100));
        });
        DeserializeOne<AuthorFull>(dir, v => {
            v.UserId.Should().Be(TestUserId);
            v.RoleIds.Should().HaveCount(1);
        });
        DeserializeOne<Contact>(dir, v => {
            v.IsPinned.Should().BeTrue();
            v.PeerContactName.Should().Be("Test Contact");
        });
        DeserializeOne<Place>(dir, v => {
            v.Title.Should().Be("Test Place");
            v.IsPublic.Should().BeTrue();
        });
        DeserializeOne<PlaceRules>(dir, v => {
            v.CanRead().Should().BeTrue();
            v.CanWrite().Should().BeTrue();
        });
        DeserializeOne<ChatPosition>(dir, v => {
            v.EntryLid.Should().Be(42);
            v.Origin.Should().Be("test-origin");
        });
        DeserializeOne<Mention>(dir, v => {
            v.Id.Should().Be((Symbol)"mention-1");
            v.EntryId.Should().Be(TestEntryId);
        });
        DeserializeOne<UserLanguageSettings>(dir, v => {
            v.Primary.Should().Be(Languages.French);
        });
        DeserializeOne<UserOnboardingSettings>(dir, v => {
            v.IsAvatarStepCompleted.Should().BeTrue();
            v.IsCreateChatsStepCompleted.Should().BeTrue();
        });
        DeserializeOne<UserBubbleSettings>(dir, v => {
            v.ReadBubbles.Should().HaveCount(2);
        });
        DeserializeOne<ChatListSettings>(dir, v => {
            v.Order.Should().Be(ChatListOrder.ByAlphabet);
        });
        DeserializeOne<ActiveChat>(dir, v => {
            v.ChatId.Should().Be(TestChatId);
        });
        DeserializeOne<UserPresences_CheckIn>(dir, v => {
            v.IsActive.Should().BeTrue();
        });
    }

    // Serialize/DeserializeOne

    private void SerializeOne<T>(T value, string dir)
    {
        var path = Path.Combine(dir, typeof(T).Name + ".bin");
        var s = Serializer.ToTyped<T>();
        using var buffer = s.Write(value);
        File.WriteAllBytes(path, buffer.WrittenMemory.ToArray());
        WriteLine($"Serialized {typeof(T).Name} -> {path} ({buffer.WrittenMemory.Length} bytes)");
    }

    private void DeserializeOne<T>(string dir, Action<T> assert)
    {
        var path = Path.Combine(dir, typeof(T).Name + ".bin");
        var data = File.ReadAllBytes(path);
        var s = Serializer.ToTyped<T>();
        var result = s.Read(data, out _);
        result.Should().NotBeNull();
        assert(result);
        WriteLine($"Deserialized {typeof(T).Name} <- {path} ({data.Length} bytes)");
    }

    // RPC API return types - factory methods

    private AccountFull CreateAccountFull()
        => new(new User(TestUserId.Value, "Test User"), 1) {
            IsAdmin = false,
            Email = "test@example.com",
            Name = "Test User",
            Phone = Phone.Parse("1-5551234567"),
            IsGreetingCompleted = true,
            CreatedAt = TestMoment,
            TimeZone = "America/New_York",
            Status = AccountStatus.Active,
            Avatar = new Avatar("avatar-1") { Name = "Test User", Bio = "Hello!" },
        };

    private SessionInfo CreateSessionInfo()
        => new(Session.New(), TestMoment) {
            Version = 1,
            LastSeenAt = TestMoment,
            IPAddress = "127.0.0.1",
            UserAgent = "TestAgent/1.0",
        };

    private ChatModel CreateChat()
        => new(TestChatId, 1) {
            Title = "Test Chat",
            CreatedAt = TestMoment,
            IsPublic = true,
            AllowGuestAuthors = false,
            AllowAnonymousAuthors = false,
            Description = "A test chat for evolution testing",
            SystemTag = "general",
        };

    private AuthorRules CreateAuthorRules()
        => new(TestChatId, null, null, ChatPermissions.Read | ChatPermissions.Write);

    private ChatNews CreateChatNews()
        => new(new Range<long>(1, 100), CreateChatEntry());

    private ChatEntry CreateChatEntry()
        => new(TestEntryId, 1) {
            AuthorId = TestAuthorId,
            BeginsAt = TestMoment,
            Content = "Hello, world!",
            HasReactions = true,
        };

    private ChatTile CreateChatTile()
    {
        var entries = new[] { CreateChatEntry() };
        return new ChatTile(new Range<long>(0, 100), false, entries);
    }

    private AuthorFull CreateAuthorFull()
        => new(TestUserId, TestAuthorId, 1) {
            AvatarId = "avatar-1",
            IsAnonymous = false,
            HasLeft = false,
            Avatar = new Avatar("avatar-1") { Name = "Test User" },
            RoleIds = [RoleId.New(TestChatId, 1)],
            CreatedAt = TestMoment,
        };

    private Contact CreateContact()
    {
        ChatId groupChatId = GroupChatId.New();
        return new Contact(ContactId.NewAny(TestUserId, groupChatId), 1) {
            UserId = TestUserId,
            TouchedAt = TestMoment,
            IsPinned = true,
            PeerContactName = "Test Contact",
        };
    }

    private Place CreatePlace()
        => new(TestPlaceId, 1) {
            Title = "Test Place",
            CreatedAt = TestMoment,
            IsPublic = true,
            Description = "A test place",
        };

    private PlaceRules CreatePlaceRules()
        => new(TestPlaceId, null, null, PlacePermissions.Read | PlacePermissions.Write);

    private ChatPosition CreateChatPosition()
        => new(42, "test-origin");

    private Mention CreateMention()
        => new() {
            Id = "mention-1",
            EntryId = TestEntryId,
            MentionId = MentionId.NewUser(TestUserId),
        };

    // Kvas settings types - factory methods

    private UserLanguageSettings CreateUserLanguageSettings()
        => new() { Primary = Languages.French };

    private UserOnboardingSettings CreateUserOnboardingSettings()
        => new() {
            IsAvatarStepCompleted = true,
            IsCreateChatsStepCompleted = true,
            IsVerifyPhoneStepCompleted = false,
        };

    private UserBubbleSettings CreateUserBubbleSettings()
        => new() { ReadBubbles = ["bubble-1", "bubble-2"] };

    private ChatListSettings CreateChatListSettings()
        => new(ChatListOrder.ByAlphabet);

    private ActiveChat CreateActiveChat()
        => new(TestChatId);

    // Command type

    private UserPresences_CheckIn CreateUserPresencesCheckIn()
        => new(Session.New(), true);
}
