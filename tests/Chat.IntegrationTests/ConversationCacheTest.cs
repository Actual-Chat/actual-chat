using ActualChat.Live;
using ActualChat.Streaming;
using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

// Guards the conversation metadata cache against live-session churn. ConversationsBackend reaches
// LiveSessionsBackend.GetState - invalidated ~3x/min per session by its liveness poll - through
// GetVisibleStartLid/GetLiveConversationSnapshot, which consolidate: they recompute on that churn and
// swallow the invalidation when neither the block's start nor its card actually moved. Consolidation
// runs on a background task, so these tests wait rather than assert synchronously.

[Collection(nameof(ChatCollection))]
public class ConversationCacheTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private static readonly TimeSpan SettleDelay = TimeSpan.FromSeconds(2);
    private static TileStack<long> IdTileStack => Constants.Chat.ServerIdTileStack;
    private BlazorTester Tester => field ??= AppHost.NewBlazorTester(Out);

    protected override async Task DisposeAsync()
    {
        await Tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task RangeMetaSurvivesLiveStateChangeThatCannotMoveTheBlock()
    {
        // arrange
        var (chatId, tileStart, live, conversations) = await StartLiveSession();

        var cRangeMeta = await Computed.Capture(() => conversations.GetRangeMeta(chatId, tileStart, default));
        var cTile = await Computed.Capture(
            () => conversations.GetTile(chatId, IdTileStack.LastLayer.GetTile(tileStart).Range, default));
        cRangeMeta.IsConsistent().Should().BeTrue();
        cTile.IsConsistent().Should().BeTrue();
        var whenRangeMetaInvalidated = cRangeMeta.WhenInvalidated();
        var whenTileInvalidated = cTile.WhenInvalidated();

        // act - SetRules invalidates GetState but cannot move the block's start or change its card
        await live.SetRules(chatId, new SessionRules { VideoAllowed = false }, default);

        // assert
        var cState = await Computed.Capture(() => live.GetState(chatId, default));
        cState.Value!.Rules.VideoAllowed.Should().BeFalse("the probe must actually have changed the state");
        await Task.WhenAny(whenRangeMetaInvalidated, whenTileInvalidated, Task.Delay(SettleDelay));
        whenRangeMetaInvalidated.IsCompleted.Should()
            .BeFalse("live-session churn must not invalidate conversation ranges");
        whenTileInvalidated.IsCompleted.Should()
            .BeFalse("live-session churn must not invalidate the conversation tile");
    }

    [Fact]
    public async Task RangeMetaSurvivesUnchangedSummary()
    {
        // arrange
        var (chatId, tileStart, live, conversations) = await StartLiveSession();
        var summary = new LiveSessionSummary {
            Title = "Standup",
            Description = "d",
            Summary = "s",
            EndEntryLid = 1,
            MessageCount = 1,
        };
        await live.UpdateSummary(chatId, summary, default);

        var cRangeMeta = await Computed.Capture(() => conversations.GetRangeMeta(chatId, tileStart, default));
        var cLiveConversation = await Computed.Capture(() => live.GetLiveConversation(chatId, default));
        cRangeMeta.IsConsistent().Should().BeTrue();
        var whenRangeMetaInvalidated = cRangeMeta.WhenInvalidated();
        var whenLiveConversationInvalidated = cLiveConversation.WhenInvalidated();

        // act - the summary flow re-runs on a schedule and usually carries the very same summary
        await live.UpdateSummary(chatId, summary, default);

        // assert
        await Task.WhenAny(whenRangeMetaInvalidated, whenLiveConversationInvalidated, Task.Delay(SettleDelay));
        whenRangeMetaInvalidated.IsCompleted.Should()
            .BeFalse("an unchanged summary must not invalidate conversation ranges");
        whenLiveConversationInvalidated.IsCompleted.Should()
            .BeFalse("an unchanged summary must not invalidate the live card");
    }

    [Fact]
    public async Task ChangedSummaryStillInvalidatesTheLiveCard()
    {
        // arrange
        var (chatId, _, live, _) = await StartLiveSession();
        await live.UpdateSummary(
            chatId,
            new LiveSessionSummary { Title = "before", EndEntryLid = 1, MessageCount = 1 },
            default);
        var cLiveConversation = await Computed.Capture(() => live.GetLiveConversation(chatId, default));
        cLiveConversation.Value!.Title.Should().Be("before");

        // act
        await live.UpdateSummary(
            chatId,
            new LiveSessionSummary { Title = "after", EndEntryLid = 2, MessageCount = 2 },
            default);

        // assert
        await ComputedTest.When(async ct => {
            var conversation = await live.GetLiveConversation(chatId, ct);
            conversation!.Title.Should().Be("after");
        });
        cLiveConversation.IsConsistent().Should().BeFalse();
    }

    // Private methods

    private async Task<(ChatId ChatId, long TileStart, ILiveSessionsBackend Live, IConversationsBackend Conversations)>
        StartLiveSession()
    {
        await Tester.SignInAsUniqueBob();
        var (chatId, _) = await Tester.CreateChat(true);
        var author = await Tester.AppServices.GetRequiredService<IAuthors>().GetOwn(Tester.Session, chatId, default);
        var live = Tester.AppServices.GetRequiredService<ILiveSessionsBackend>();
        var conversations = Tester.AppServices.GetRequiredService<IConversationsBackend>();

        // Two streamers latch the session - only a latched one owns a range and renders a card.
        await live.OnStreamRegistered(chatId, author!.Id, null, true, true, default);
        await live.OnStreamRegistered(chatId, AuthorId.New(chatId, 777_071), null, true, true, default);
        var state = await live.GetState(chatId, default);
        state!.SessionStartedAt.Should().NotBeNull("the session must latch or these tests don't bite");

        return (chatId, IdTileStack.LastLayer.GetTile(state.EffectiveVisibleStartLid).Start, live, conversations);
    }
}
