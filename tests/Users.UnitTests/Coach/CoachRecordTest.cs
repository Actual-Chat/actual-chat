using ActualChat.Chat;
using ActualChat.Hashing;

namespace ActualChat.Users.UnitTests.Coach;

public class CoachRecordTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void FromEntryShouldCarryCountsAndStripSynonyms()
    {
        // arrange
        var chatId = GroupChatId.New();
        var analysis = new CoachEntryAnalysis(ChatEntryId.New(chatId, 7), 1) {
            AuthorId = AuthorId.New(chatId, 1),
            UserId = UserId.New(),
            BeginsAt = new DateTime(2026, 9, 26, 10, 30, 0, DateTimeKind.Utc),
            Language = Languages.English,
            DurationSeconds = 12,
            Words = 20,
            Spans = ApiArray.New(new SpeechSpan(SpeechSpanKind.Weak, "awesome", 3, 7, ApiArray.New("excellent"))),
            WeakWords = 1,
            TagState = CoachTagState.Tagged,
            ContentHash = ChatEntryHashExt.GetContentHashString("x"),
        };

        // act
        var record = CoachRecord.FromEntry(analysis);

        // assert
        record.Kind.Should().Be(CoachRecordKind.Entry);
        record.SourceId.Should().Be(analysis.Id.Value);
        record.ChatId.Should().Be(chatId);
        record.Day.Should().Be(new DateTime(2026, 9, 26, 0, 0, 0, DateTimeKind.Utc));
        record.Version.Should().Be(1, "the chat-side version orders late and duplicate deliveries");
        record.Entry!.Words.Should().Be(20);
        record.Entry.IsTagged.Should().BeTrue();
        record.Entry.Spans.Should().ContainSingle().Which.Synonyms.Should().BeEmpty("synonyms stay chat-side");
        record.Run.Should().BeNull();
    }

    [Fact]
    public void FromRunShouldKeyByRunAndAuthor()
    {
        // arrange
        var chatId = GroupChatId.New();
        var analysis = new CoachConversationAnalysis(ConversationId.New(chatId, 100), AuthorId.New(chatId, 2), 1) {
            UserId = UserId.New(),
            EndsAt = new DateTime(2026, 9, 26, 23, 59, 0, DateTimeKind.Utc),
            OwnSpeechSeconds = 30,
            TotalSpeechSeconds = 90,
            Participants = 3,
        };

        // act
        var record = CoachRecord.FromRun(analysis);

        // assert
        record.Kind.Should().Be(CoachRecordKind.Run);
        record.SourceId.Should().Be($"{analysis.Id}:{analysis.AuthorId}");
        record.Run!.OwnSpeechSeconds.Should().Be(30);
        record.Entry.Should().BeNull();
    }

    [Fact]
    public void RecordShouldPassThroughAllSerializers()
    {
        // arrange
        var chatId = GroupChatId.New();
        var record = CoachRecord.FromEntry(new CoachEntryAnalysis(ChatEntryId.New(chatId, 1), 1) {
            AuthorId = AuthorId.New(chatId, 1),
            UserId = UserId.New(),
            Words = 3,
            Spans = ApiArray.New(new SpeechSpan(SpeechSpanKind.Filler, "like", 0, 4, ApiArray<string>.Empty)),
            ContentHash = ChatEntryHashExt.GetContentHashString("x"),
        });

        // act + assert
        record.AssertPassesThroughSerializers((restored, original) => {
            restored.SourceId.Should().Be(original.SourceId);
            restored.Entry!.Words.Should().Be(3);
            restored.Entry.Spans.Should().Equal(original.Entry!.Spans);
        }, Out);
    }
}
