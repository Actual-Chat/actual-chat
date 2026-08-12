using ActualChat.Live;
using ActualChat.Streaming;
using ActualChat.Testing.Host;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

// The chat view rebuilds whenever anything GetChatItems depends on is invalidated, and each rebuild costs
// a chain of RPC round-trips. These tests pin the client-side half of that dependency set: live-session
// churn that cannot change what's rendered must not drop the metadata the view was built from.
[Collection(nameof(ChatUICollection))]
public sealed class ChatUICacheTest(ChatAppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ChatAppHostFixture>(fixture, @out)
{
    private BlazorTester Tester => field ??= AppHost.NewBlazorTester(Out);

    protected override async Task DisposeAsync()
    {
        await Tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task ChatViewMetadataSurvivesLiveStateChurn()
    {
        // arrange
        await Tester.SignInAsUniqueBob();
        var (chat, _) = await Tester.CreateAndGetChat(false, "chat-ui-cache-test");
        for (var i = 0; i < 3; i++)
            await Tester.CreateTextEntry(chat.Id, $"entry {i}");
        var author = await Tester.GetOwnAuthor(chat.Id).Require();
        var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
        await liveBackend.OnStreamRegistered(chat.Id, author.Id, null, true, true, CancellationToken.None);
        await liveBackend
            .OnStreamRegistered(chat.Id, AuthorId.New(chat.Id, 777_072), null, true, true, CancellationToken.None);
        var live = await liveBackend.GetState(chat.Id, CancellationToken.None);
        live!.SessionStartedAt.Should().NotBeNull("the session must latch or this test doesn't bite");

        // Build the view once so the whole client-side chain is warm.
        var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
        var idRange = await Tester.Chats.GetIdRange(Tester.Session, chat.Id, CancellationToken.None);
        var query = new ChatDataQuery(idRange, -chatUI.HalfLoadLimit, chatUI.HalfLoadLimit);
        var items = await chatUI.GetChatItems(chat.Id, query, 0, CancellationToken.None);
        items.Items.Should().NotBeEmpty();

        var tileStart = ChatUI.ServerIdTileStack.LastLayer.GetTile(live.EffectiveVisibleStartLid).Start;
        var cRangeMeta = await Computed.Capture(
            () => Tester.Chats.GetChatRangeMeta(Tester.Session, chat.Id, tileStart, CancellationToken.None));
        cRangeMeta.IsConsistent().Should().BeTrue();

        // act - rewrites the live session state, but cannot change a single rendered row
        await liveBackend.SetRules(chat.Id, new SessionRules { VideoAllowed = false }, CancellationToken.None);

        // assert
        cRangeMeta.IsConsistent().Should()
            .BeTrue("live-session churn must not force the chat view to reload its range metadata");
    }

    [Fact]
    public async Task RangeMetaSurvivesEntryContentUpdate()
    {
        // arrange - mirrors the transcription pipeline: ChangeEntry Create at utterance start,
        // ChangeEntry Update at finalization
        await Tester.SignInAsUniqueBob();
        var (chat, _) = await Tester.CreateAndGetChat(false, "chat-ui-cache-test-update");
        var streamingEntry = await Tester.CreateStreamingEntry(chat.Id, Languages.English);
        var entryLid = streamingEntry.ChatEntrySlim.Id.LocalId;
        var tileStart = ChatUI.ServerIdTileStack.LastLayer.GetTile(entryLid).Start;
        var cRangeMeta = await Computed.Capture(
            () => Tester.Chats.GetChatRangeMeta(Tester.Session, chat.Id, tileStart, CancellationToken.None));
        cRangeMeta.IsConsistent().Should().BeTrue();

        // act - a content-only update; entry lids don't change, so the range meta can't either
        await Tester.FinalizeStreamingEntry(streamingEntry, "final transcript");

        // assert
        cRangeMeta.IsConsistent().Should()
            .BeTrue("a content update cannot change lid structure, so range meta must stay cached");

        // positive control - a new entry does change lid structure and must invalidate it
        await Tester.CreateTextEntry(chat.Id, "next entry");
        await cRangeMeta.WhenInvalidated(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
    }
}
