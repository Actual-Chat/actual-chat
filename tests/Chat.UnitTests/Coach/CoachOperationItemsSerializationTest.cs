using ActualChat.Chat.Coach;
using ActualChat.Hashing;

namespace ActualChat.Chat.UnitTests.Coach;

// Both values are set into CommandContext.Operation.Items during a command's write phase and read
// back in its invalidation phase, possibly on another node via _Operations.ItemsJson (Newtonsoft).
public class CoachOperationItemsSerializationTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void RunTouchShouldRoundTripViaOperationItems()
    {
        // arrange
        var chatId = GroupChatId.New();
        var touch = new CoachRunTouch(
            ConversationId.New(chatId, 10),
            ApiArray.New(AuthorId.New(chatId, 1), AuthorId.New(chatId, 2)),
            ApiArray.New(new CoachTaggedEntry(ChatEntryId.New(chatId, 11), AuthorId.New(chatId, 1))));
        var bag = new MutablePropertyBag();
        bag.KeylessSet(touch);

        // act
        var json = NewtonsoftJsonSerializer.Default.Write(bag.Snapshot);
        var got = NewtonsoftJsonSerializer.Default.Read<PropertyBag>(json).ToMutable().KeylessGet<CoachRunTouch>();

        // assert
        got.Should().NotBeNull();
        got!.Id.Should().Be(touch.Id);
        got.AuthorIds.Should().Equal(touch.AuthorIds);
        got.TaggedEntries.Should().Equal(touch.TaggedEntries);
    }

    [Fact]
    public void EntryAnalysisShouldRoundTripViaOperationItems()
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
        var bag = new MutablePropertyBag();
        bag.KeylessSet(analysis);

        // act
        var json = NewtonsoftJsonSerializer.Default.Write(bag.Snapshot);
        var got = NewtonsoftJsonSerializer.Default.Read<PropertyBag>(json).ToMutable().KeylessGet<CoachEntryAnalysis>();

        // assert
        got.Should().NotBeNull();
        got!.Id.Should().Be(analysis.Id);
        got.AuthorId.Should().Be(analysis.AuthorId);
        got.Spans.Should().Equal(analysis.Spans);
        got.ContentHash.Should().Be(analysis.ContentHash);
    }
}
