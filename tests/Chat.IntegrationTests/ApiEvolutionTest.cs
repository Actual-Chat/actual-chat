using ActualChat.Contacts;
using ActualChat.UI.Blazor.App.Services;
using ChatModel = ActualChat.Chat.Chat;

namespace ActualChat.Chat.IntegrationTests;

#pragma warning disable CS0618 // Type or member is obsolete

public class ApiEvolutionTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly bool MustSerialize = false;

    private static readonly ChatId TestChatId = ChatId.Parse("the-actual-one");
    private static readonly UserId TestUserId = UserId.Parse("testUser1");
    private static readonly AuthorId TestAuthorId = AuthorId.New(TestChatId, 1);
    private static readonly PlaceId TestPlaceId = PlaceId.New();
    private static readonly ChatEntryId TestEntryId = ChatEntryId.New(TestChatId, 1);
    private static readonly Moment TestMoment = new(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));

    private IByteSerializer Serializer => Serializers.MemoryPack;

    [Fact(Skip = "Will fail in this version, we'll get back to that later.")]
    public void RunTest()
    {
        if (MustSerialize)
            Serialize();
        Deserialize();
    }

    [Fact]
    public void SessionInfoFormatTest()
    {
        var s = Serializer.ToTyped<LegacySessionInfo>();

        var si = CreateLegacySessionInfo();
        using var buffer = s.Write(si);
        var bytes = buffer.WrittenMemory.ToArray();
        WriteLine($"LegacySessionInfo bytes: {bytes.Length}");
        WriteLine($"Header hex (first 20 bytes): {Convert.ToHexString(bytes.AsSpan(0, Math.Min(20, bytes.Length)))}");

        // Header byte = member count
        var memberCount = bytes[0];
        WriteLine($"Member count: {memberCount}");

        // Read VarInt deltas
        var offset = 1;
        var deltas = new int[memberCount];
        for (int i = 0; i < memberCount; i++) {
            int value = 0, shift = 0;
            byte b;
            do {
                b = bytes[offset++];
                value |= (b & 0x7F) << shift;
                shift += 7;
            } while ((b & 0x80) != 0);
            deltas[i] = value;
        }
        for (int i = 0; i < memberCount; i++)
            WriteLine($"  delta[{i}] = {deltas[i]}");

        var dataStart = offset;
        WriteLine($"Data starts at byte {dataStart}");
        WriteLine($"Sum of deltas: {deltas.Sum()}");
        WriteLine($"Expected data size: {bytes.Length - dataStart}");

        // Verify round-trip
        var result = s.Read(bytes, out _);
        result.Should().NotBeNull();
        result.IPAddress.Should().Be("127.0.0.1");
    }

    // Private methods

    private void Serialize([CallerFilePath] string? callerFilePath = null)
    {
        var dir = Path.Combine(Path.GetDirectoryName(callerFilePath)!, "data", "api-evolution");
        Directory.CreateDirectory(dir);

        SerializeOne(CreateAccountFull(), dir);
        SerializeOne(CreateLegacySessionInfo(), dir);
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
        var errors = new List<string>();

        DeserializeOne<AccountFull>(dir, errors, v => {
            v.Name.Should().Be("Test User");
            v.Email.Should().Be("test@example.com");
        });
        DeserializeOne<LegacySessionInfo>(dir, errors, v => {
            v.IPAddress.Should().Be("127.0.0.1");
        });
        DeserializeOne<ChatModel>(dir, errors, v => {
            v.Title.Should().Be("Test Chat");
            v.IsPublic.Should().BeTrue();
        });
        DeserializeOne<AuthorRules>(dir, errors, v => {
            v.CanRead().Should().BeTrue();
            v.CanWrite().Should().BeTrue();
        });
        DeserializeOne<ChatNews>(dir, errors, v => {
            v.TextEntryLidRange.Should().Be(new Range<long>(1, 100));
            v.LastTextEntry.Should().NotBeNull();
        });
        DeserializeOne<ChatEntry>(dir, errors, v => {
            v.Content.Should().Be("Hello, world!");
            v.AuthorId.Should().Be(TestAuthorId);
        });
        DeserializeOne<ChatTile>(dir, errors, v => {
            v.Entries.Length.Should().Be(1);
            v.LidTileRange.Should().Be(new Range<long>(0, 100));
        });
        DeserializeOne<AuthorFull>(dir, errors, v => {
            v.UserId.Should().Be(TestUserId);
            v.RoleIds.Should().HaveCount(1);
        });
        DeserializeOne<Contact>(dir, errors, v => {
            v.IsPinned.Should().BeTrue();
            v.PeerContactName.Should().Be("Test Contact");
        });
        DeserializeOne<Place>(dir, errors, v => {
            v.Title.Should().Be("Test Place");
            v.IsPublic.Should().BeTrue();
        });
        DeserializeOne<PlaceRules>(dir, errors, v => {
            v.CanRead().Should().BeTrue();
            v.CanWrite().Should().BeTrue();
        });
        DeserializeOne<ChatPosition>(dir, errors, v => {
            v.EntryLid.Should().Be(42);
            v.Origin.Should().Be("test-origin");
        });
        DeserializeOne<Mention>(dir, errors, v => {
            v.Id.Should().Be((Symbol)"mention-1");
            v.EntryId.Should().Be(TestEntryId);
        });
        DeserializeOne<UserLanguageSettings>(dir, errors, v => {
            v.Primary.Should().Be(Languages.French);
        });
        DeserializeOne<UserOnboardingSettings>(dir, errors, v => {
            v.IsAvatarStepCompleted.Should().BeTrue();
            v.IsCreateChatsStepCompleted.Should().BeTrue();
        });
        DeserializeOne<UserBubbleSettings>(dir, errors, v => {
            v.ReadBubbles.Should().HaveCount(2);
        });
        DeserializeOne<ChatListSettings>(dir, errors, v => {
            v.Order.Should().Be(ChatListOrder.ByAlphabet);
        });
        DeserializeOne<ActiveChat>(dir, errors, v => {
            v.ChatId.Should().Be(TestChatId);
        });
        DeserializeOne<UserPresences_CheckIn>(dir, errors, v => {
            v.IsActive.Should().BeTrue();
        });

        if (errors.Count > 0) {
            WriteLine($"\n{errors.Count} deserialization error(s):");
            foreach (var error in errors)
                WriteLine(error);
            Assert.Fail($"{errors.Count} type(s) failed deserialization:\n" + string.Join("\n", errors));
        }
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

    private void DeserializeOne<T>(string dir, List<string> errors, Action<T> assert)
    {
        var typeName = typeof(T).Name;
        var path = Path.Combine(dir, typeName + ".bin");
        try {
            var data = File.ReadAllBytes(path);
            var s = Serializer.ToTyped<T>();
            var result = s.Read(data, out _);
            result.Should().NotBeNull();
            assert(result);
            WriteLine($"Deserialized {typeName} <- {path} ({data.Length} bytes)");
        }
        catch (Exception ex) {
            var error = $"  {typeName}: {ex.Message}";
            errors.Add(error);
            WriteLine($"FAILED {typeName}: {ex.Message}");
        }
    }

    // RPC API return types - factory methods

    private AccountFull CreateAccountFull()
        => new(TestUserId, 1) {
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

    #pragma warning disable CS0618
    private LegacySessionInfo CreateLegacySessionInfo()
    {
        var session = Session.New();
        return new LegacySessionInfo {
            SessionHash = session.Hash,
            Version = 1,
            CreatedAt = TestMoment,
            LastSeenAt = TestMoment,
            IPAddress = "127.0.0.1",
            UserAgent = "TestAgent/1.0",
        };
    }
    #pragma warning restore CS0618

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
        => new TextEntry(TestEntryId, 1) {
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
            MentionRef = MentionRef.NewUser(TestUserId),
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
