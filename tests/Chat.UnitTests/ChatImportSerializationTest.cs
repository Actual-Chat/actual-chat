namespace ActualChat.Chat.UnitTests;

public sealed class ChatImportSerializationTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void ImportModelsShouldPassThroughSerializers()
    {
        var chatId = GroupChatId.New();
        var userId = UserId.New();
        var session = new ChatImportSession(chatId, "import", userId, Moment.Now, true);
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
        var userId = UserId.New();
        var entries = new[] { new ChatImportEntry(userId, Moment.Now, "history") }.ToApiArray();
        new ChatImports_ImportEntries {
            Session = Session.New(), ChatId = chatId, ImportId = "import", Entries = entries,
        }.AssertPassesThroughSerializers((actual, expected) => actual.Should().BeEquivalentTo(expected));
        new ChatsBackend_ImportEntries(chatId, userId, "import", "batch", entries)
            .AssertPassesThroughSerializers((actual, expected) => actual.Should().BeEquivalentTo(expected));
        new ChatImportChangedEvent(chatId, "import")
            .AssertPassesThroughSerializers((actual, expected) => actual.Should().BeEquivalentTo(expected));
    }
}
