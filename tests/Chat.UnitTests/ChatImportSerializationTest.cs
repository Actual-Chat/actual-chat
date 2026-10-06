namespace ActualChat.Chat.UnitTests;

public sealed class ChatImportSerializationTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void ImportIdShouldRoundTripItsParts()
    {
        var chatId = PlaceId.New().RootChatId;
        var importId = ChatImportId.New(chatId, "a-Token_1");

        var parsed = ChatImportId.Parse(importId.Value);

        parsed.Should().Be(importId);
        parsed.ChatId.Should().Be((ChatId)chatId);
        parsed.Token.Should().Be("a-Token_1");
        parsed.ShardKey.Should().Be(chatId.ShardKey);
        importId.AssertPassesThroughSerializers();
        ChatImportId.TryParse(chatId.Value + ":").Should().BeNull();
        ChatImportId.TryParse(chatId.Value + ":a:b").Should().BeNull();
        ChatImportId.TryParse("not-a-chat-id").Should().BeNull();
        ChatImportId.TryParse(chatId.Value + ":" + new string('x', ChatImportId.MaxTokenLength + 1)).Should().BeNull();
    }

    [Fact]
    public void ImportModelsShouldPassThroughSerializers()
    {
        var chatId = GroupChatId.New();
        var userId = UserId.New();
        var session = new ChatImportSession(ChatImportId.New(chatId, "import")) {
            StartedBy = userId, StartedAt = Moment.Now,
        };
        session.AssertPassesThroughSerializers((actual, expected) => actual.Should().BeEquivalentTo(expected));
        new ChatImportConsentSummary(1, 1, [userId])
            .AssertPassesThroughSerializers((actual, expected) => actual.Should().BeEquivalentTo(expected));
        new ChatImportEntry(userId, Moment.Now, "history") {
            UploadIds = [UploadId.New()], RepliedEntryLid = 12,
        }.AssertPassesThroughSerializers((actual, expected) => actual.Should().BeEquivalentTo(expected));
        new ChatImportEntryResult(ChatEntryId.New(chatId, 12), ExceptionInfo.None)
            .AssertPassesThroughSerializers((actual, expected) => actual.Should().BeEquivalentTo(expected));
        new ChatImportEntryResult(null, new ExceptionInfo(StandardError.Constraint("consent required")))
            .AssertPassesThroughSerializers((actual, expected) => actual.Should().BeEquivalentTo(expected));
    }

    [Fact]
    public void ImportCommandsShouldPassThroughSerializers()
    {
        var chatId = GroupChatId.New();
        var importId = ChatImportId.New(chatId, "import");
        var userId = UserId.New();
        var entries = new[] { new ChatImportEntry(userId, Moment.Now, "history") }.ToApiArray();
        new ChatImports_ImportEntries {
            Session = Session.New(), ChatId = chatId, ImportId = importId, Entries = entries,
        }.AssertPassesThroughSerializers((actual, expected) => actual.Should().BeEquivalentTo(expected));
        new ChatsBackend_ImportEntries(chatId, userId, importId, "batch", entries)
            .AssertPassesThroughSerializers((actual, expected) => actual.Should().BeEquivalentTo(expected));
        new MaintenancesBackend_Set(chatId.ToMaintenanceKey(), MaintenanceMode.Import) {
            OwnerId = importId.Value, StartedBy = userId, StartedAt = Moment.Now,
        }.AssertPassesThroughSerializers((actual, expected) => actual.Should().BeEquivalentTo(expected));
        new Maintenance(MaintenanceMode.Import, importId.Value) { StartedBy = userId, StartedAt = Moment.Now }
            .AssertPassesThroughSerializers((actual, expected) => actual.Should().BeEquivalentTo(expected));
    }
}
