using ActualChat.Chat.Coach;
using ActualChat.Hashing;

namespace ActualChat.Chat.UnitTests.Coach;

// Replaces CoachOperationItemsSerializationTest: these no longer travel through Operation.Items,
// but CoachEntryAnalysis is still an event payload and the touch records stay round-trippable, so
// the round-trip is worth pinning on its own.
public class CoachSerializationTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void RunTouchShouldPassThroughAllSerializers()
    {
        // arrange
        var chatId = GroupChatId.New();
        var touch = new CoachRunTouch(
            ConversationId.New(chatId, 10),
            ApiArray.New(AuthorId.New(chatId, 1), AuthorId.New(chatId, 2)),
            ApiArray.New(new CoachTaggedEntry(ChatEntryId.New(chatId, 11), AuthorId.New(chatId, 1))));

        // act & assert
        touch.AssertPassesThroughSerializers(AssertEqual, Out);
        return;

        static void AssertEqual(CoachRunTouch actual, CoachRunTouch expected)
        {
            actual.Id.Should().Be(expected.Id);
            actual.AuthorIds.Should().Equal(expected.AuthorIds);
            actual.TaggedEntries.Should().Equal(expected.TaggedEntries);
        }
    }

    [Fact]
    public void EntryAnalysisShouldPassThroughAllSerializers()
    {
        // arrange
        var chatId = GroupChatId.New();
        var analysis = new CoachEntryAnalysis(ChatEntryId.New(chatId, 7), 3) {
            AuthorId = AuthorId.New(chatId, 1),
            UserId = UserId.New(),
            Language = Languages.Russian,
            Spans = ApiArray.New(new SpeechSpan(SpeechSpanKind.Filler, "ну", 0, 2, ApiArray<string>.Empty)),
            ContentHash = ChatEntryHashExt.GetContentHashString("ну да"),
        };

        // act & assert
        analysis.AssertPassesThroughSerializers(AssertEqual, Out);
        return;

        static void AssertEqual(CoachEntryAnalysis actual, CoachEntryAnalysis expected)
        {
            actual.Id.Should().Be(expected.Id);
            actual.AuthorId.Should().Be(expected.AuthorId);
            actual.Spans.Should().Equal(expected.Spans);
            actual.ContentHash.Should().Be(expected.ContentHash);
        }
    }
}
