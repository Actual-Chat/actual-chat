using ActualChat.Chat.Db;
using ActualChat.Hashing;

namespace ActualChat.Chat.UnitTests.Coach;

public class DbCoachEntryTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void EntryAnalysisShouldSurviveDbRoundTrip()
    {
        // arrange
        var chatId = GroupChatId.New();
        var model = new CoachEntryAnalysis(ChatEntryId.New(chatId, 7), 3) {
            AuthorId = AuthorId.New(chatId, 1),
            UserId = UserId.New(),
            BeginsAt = new DateTime(2026, 9, 25, 10, 0, 0, DateTimeKind.Utc),
            Language = Languages.Russian,
            DurationSeconds = 12.5,
            SpeechSeconds = 10,
            Words = 20,
            Sentences = 3,
            Questions = 1,
            Repetitions = 0,
            DistinctWords = 18,
            Pauses = 1,
            PauseSeconds = 2.5,
            Spans = ApiArray.New(
                new SpeechSpan(SpeechSpanKind.FilledPause, "э-э", 4, 3, ApiArray<string>.Empty),
                new SpeechSpan(SpeechSpanKind.Weak, "очень", 10, 5, ApiArray.New("крайне", "весьма"))),
            FilledPauses = 1,
            WeakWords = 1,
            TagState = CoachTagState.Tagged,
            PromptVersion = 1,
            TaggedAt = new DateTime(2026, 9, 25, 10, 0, 5, DateTimeKind.Utc),
            ContentHash = ChatEntryHashExt.GetContentHashString("So, um, hello"),
        };

        // act
        var restored = new DbCoachEntry(model).ToModel();

        // assert
        restored.Should().BeEquivalentTo(model);
    }

    [Fact]
    public void ConversationAnalysisShouldSurviveDbRoundTrip()
    {
        // arrange
        var chatId = GroupChatId.New();
        var model = new CoachConversationAnalysis(ConversationId.New(chatId, 100), AuthorId.New(chatId, 2), 5) {
            UserId = UserId.New(),
            ConversationVersion = 9,
            EndsAt = new DateTime(2026, 9, 25, 11, 0, 0, DateTimeKind.Utc),
            OwnSpeechSeconds = 30,
            TotalSpeechSeconds = 90,
            OwnTurns = 3,
            TotalTurns = 7,
            Participants = 3,
            LongestMonologueSeconds = 15,
            Responses = 2,
            ResponseGapSeconds = 0.8,
            Interruptions = 1,
        };

        // act
        var restored = new DbCoachConversation(model).ToModel();

        // assert
        restored.Should().BeEquivalentTo(model);
    }
}
