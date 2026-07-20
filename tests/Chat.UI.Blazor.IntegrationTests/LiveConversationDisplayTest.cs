using ActualChat.Live;
using ActualChat.Streaming;
using ActualChat.Testing.Host;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Components;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

[Collection(nameof(ChatUICollection))]
public sealed class LiveConversationDisplayTest(ChatAppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ChatAppHostFixture>(fixture, @out)
{
    private BlazorTester Tester => field ??= AppHost.NewBlazorTester(Out);

    protected override async Task DisposeAsync()
    {
        await Tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task ShouldSurfaceVideoOnlyLiveBlockStartingAViewTile()
    {
        // The block sits at a lid only transcription ever fills, so video-only leaves its tile unloaded.

        // arrange
        await Tester.SignInAsUniqueBob();
        var (chat, _) = await Tester.CreateAndGetChat(false, "live-block-boundary-test");
        var tileSize = ChatUI.IdTileStack.FirstLayer.TileSize;
        ChatEntry entry;
        do {
            entry = await Tester.CreateTextEntry(chat.Id, "filler");
        } while ((entry.LocalId + 1) % tileSize != 0);
        var author = await Tester.GetOwnAuthor(chat.Id).Require();
        var peerId = AuthorId.New(chat.Id, 777_070);
        var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();

        // act
        await liveBackend.OnStreamRegistered(chat.Id, author.Id, null, false, CancellationToken.None);
        await liveBackend.OnStreamRegistered(chat.Id, peerId, null, false, CancellationToken.None);
        var live = await liveBackend.GetState(chat.Id, CancellationToken.None);
        var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
        var idRange = await Tester.Chats.GetIdRange(Tester.Session, chat.Id, CancellationToken.None);
        var query = new ChatDataQuery(idRange, -chatUI.HalfLoadLimit, chatUI.HalfLoadLimit);
        var items = await chatUI.GetChatItems(chat.Id, query, 0, CancellationToken.None);

        // assert
        live.Should().NotBeNull();
        live!.SessionStartedAt.Should().NotBeNull();
        (live.VisibleStartLid % tileSize).Should()
            .Be(0L, "the live block must start a fresh view tile or this test doesn't bite");
        items.Items.OfType<ConversationMessage>()
            .Select(m => m.Conversation!.Id)
            .Should().Contain(live.ConversationId);
    }

    [Fact]
    public async Task ShouldFlagFirstEntryOfAnExpandedConversation()
    {
        // The header already spaces the block off, so its first entry drops the block-start padding.

        // arrange
        await Tester.SignInAsUniqueBob();
        var (chat, _) = await Tester.CreateAndGetChat(true, "conv-first-entry-test");
        var first = await Tester.CreateTextEntry(chat.Id, "opens the conversation");
        ChatEntry last = first;
        for (var i = 0; i < 4; i++)
            last = await Tester.CreateTextEntry(chat.Id, $"body-{i}");
        var conversationId = ConversationId.New(chat.Id, first.LocalId);
        var conversation = new Conversation(conversationId) {
            Title = "Recap",
            Summary = "s",
            Description = "d",
            EndEntryLid = last.LocalId,
            MessageCount = 5,
            IsExpandedByDefault = true,
        };
        await Tester.Commander.Call(new ConversationBackend_Materialize(conversation), CancellationToken.None);

        // act
        var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
        var idRange = await Tester.Chats.GetIdRange(Tester.Session, chat.Id, CancellationToken.None);
        var query = new ChatDataQuery(idRange, -chatUI.HalfLoadLimit, chatUI.HalfLoadLimit);
        var items = await chatUI.GetChatItems(chat.Id, query, 0, CancellationToken.None);
        var entries = items.Items.SelectMany(i => i.GetLeafMessages())
            .OfType<ChatEntryMessage>()
            .Where(m => m.Kind == ChatMessageKind.None)
            .ToList();

        // assert
        items.Items.SelectMany(i => i.GetLeafMessages()).OfType<ConversationHeader>()
            .Should().NotBeEmpty("the conversation must render expanded");
        var opener = entries.Single(m => m.Id == first.LocalId);
        opener.Flags.Should().HaveFlag(ChatMessageFlags.BlockStart, "it still shows its author");
        opener.Flags.Should().HaveFlag(ChatMessageFlags.FirstInConversation);
        entries.Where(m => m.Id != first.LocalId)
            .Should().NotContain(m => m.Flags.HasFlag(ChatMessageFlags.FirstInConversation));
    }

    [Fact]
    public async Task ShouldNotDuplicateJoinedLiveCardAcrossTiles()
    {
        // A landed summary stretches the card's range across tiles, and each re-emits it under one @key.

        // arrange
        await Tester.SignInAsUniqueBob();
        var (chat, _) = await Tester.CreateAndGetChat(false, "live-dup-key-test");
        var author = await Tester.GetOwnAuthor(chat.Id).Require();
        var peerId = AuthorId.New(chat.Id, 777_080);
        var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
        await liveBackend.OnStreamRegistered(chat.Id, author.Id, null, true, CancellationToken.None);
        await liveBackend.OnStreamRegistered(chat.Id, peerId, null, true, CancellationToken.None);
        var live = await liveBackend.GetState(chat.Id, CancellationToken.None);
        live!.SessionStartedAt.Should().NotBeNull();

        var tileSize = (int)ChatUI.IdTileStack.FirstLayer.TileSize;
        ChatEntry lastEntry = null!;
        for (var i = 0; i < tileSize * 3; i++)
            lastEntry = await Tester.CreateTextEntry(chat.Id, $"live-{i}");
        var summary = new LiveSessionSummary {
            Title = "Recap",
            Description = "d",
            Summary = "s",
            EndEntryLid = lastEntry.LocalId,
            MessageCount = tileSize * 3,
            IsExpandedByDefault = true,
        };
        await liveBackend.UpdateSummary(chat.Id, summary, CancellationToken.None);

        // act
        var chatAudioUI = Tester.ScopedAppServices.GetRequiredService<ChatAudioUI>();
        await chatAudioUI.SetListeningState(chat.Id, true);
        var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
        var idRange = await Tester.Chats.GetIdRange(Tester.Session, chat.Id, CancellationToken.None);
        var query = new ChatDataQuery(idRange, -chatUI.HalfLoadLimit, chatUI.HalfLoadLimit);
        var items = await chatUI.GetChatItems(chat.Id, query, 0, CancellationToken.None);

        // assert
        items.Items.OfType<ExpandedConversationMessage>().Should().NotBeEmpty("the viewer must be joined");
        items.Items
            .Select(i => ((IVirtualListItem)i).RenderKey)
            .Should().OnlyHaveUniqueItems();
        foreach (var block in items.Items.OfType<ExpandedConversationMessage>())
            block.Items
                .Select(i => ((IVirtualListItem)i).RenderKey)
                .Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task GovernorLatchesInitialFoldBoundary()
    {
        // The first observation of a chat must latch to the raw fold end as-is - a fresh render
        // starts fully folded, and only later advances go through the lag + viewport governor.

        // arrange
        await Tester.SignInAsUniqueBob();
        var (chat, _) = await Tester.CreateAndGetChat(false, "Governor latch");
        for (var i = 0; i < 5; i++)
            await Tester.CreateTextEntry(chat.Id, $"entry {i}");
        var author = await Tester.GetOwnAuthor(chat.Id).Require();
        var peerId = AuthorId.New(chat.Id, 777_090);
        var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
        await liveBackend.OnStreamRegistered(chat.Id, author.Id, null, true, CancellationToken.None);
        await liveBackend.OnStreamRegistered(chat.Id, peerId, null, true, CancellationToken.None);
        var live = await liveBackend.GetState(chat.Id, CancellationToken.None);
        live.Should().NotBeNull();
        var v = live!.EffectiveVisibleStartLid;
        await liveBackend.UpdateSummary(chat.Id,
            new LiveSessionSummary {
                Title = "Latch", Description = "d", Summary = "s",
                EndEntryLid = v + 2, MessageCount = 3,
            }, CancellationToken.None);

        // act
        var liveBlockUI = Tester.ScopedAppServices.GetRequiredService<LiveBlockUI>();
        var blockState = await liveBlockUI.GetBlockState(chat.Id, CancellationToken.None);

        // assert
        blockState.FoldBoundaryLid.Should().Be(v + 3);
        blockState.Overlay.Should().BeNull();
    }
}
