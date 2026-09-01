using System.Text;
using ActualChat.Live;
using ActualChat.Streaming;
using ActualChat.Testing.Host;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.App.Services;

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
        var chat = await CreateSettledChat("live-block-boundary-test");
        var tileSize = ChatUI.EntryIdTileLayer.TileSize;
        ChatEntry entry;
        do {
            entry = await Tester.CreateTextEntry(chat.Id, "filler");
        } while ((entry.LocalId + 1) % tileSize != 0);
        var author = await Tester.GetOwnAuthor(chat.Id).Require();
        var peerId = AuthorId.New(chat.Id, 777_070);
        var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();

        // act
        await liveBackend.OnStreamRegistered(chat.Id, author.Id, null, false, true, CancellationToken.None);
        await liveBackend.OnStreamRegistered(chat.Id, peerId, null, false, true, CancellationToken.None);
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
        // The unjoined live block now renders through the unified sticky-header shell, so its card is a
        // leaf inside the block rather than a top-level ConversationMessage.
        items.Items.SelectMany(i => i.GetLeafMessages())
            .OfType<ConversationMessage>()
            .Select(m => m.Conversation!.Id)
            .Should().Contain(live.ConversationId);
    }

    [Fact]
    public async Task ShouldFlagFirstEntryOfAnExpandedConversation()
    {
        // The header already spaces the block off, so its first entry drops the block-start padding.

        // arrange
        await Tester.SignInAsUniqueBob();
        var chat = await CreateSettledChat("conv-first-entry-test", isPublic: true);
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
        var chat = await CreateSettledChat("live-dup-key-test");
        var author = await Tester.GetOwnAuthor(chat.Id).Require();
        var peerId = AuthorId.New(chat.Id, 777_080);
        var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
        await liveBackend.OnStreamRegistered(chat.Id, author.Id, null, true, true, CancellationToken.None);
        await liveBackend.OnStreamRegistered(chat.Id, peerId, null, true, true, CancellationToken.None);
        var live = await liveBackend.GetState(chat.Id, CancellationToken.None);
        live!.SessionStartedAt.Should().NotBeNull();

        var tileSize = (int)ChatUI.EntryIdTileLayer.TileSize;
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
        // starts fully folded, and only later advances go through the viewport governor.

        // arrange
        await Tester.SignInAsUniqueBob();
        var chat = await CreateSettledChat("Governor latch");
        for (var i = 0; i < 5; i++)
            await Tester.CreateTextEntry(chat.Id, $"entry {i}");
        var author = await Tester.GetOwnAuthor(chat.Id).Require();
        var peerId = AuthorId.New(chat.Id, 777_090);
        var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
        await liveBackend.OnStreamRegistered(chat.Id, author.Id, null, true, true, CancellationToken.None);
        await liveBackend.OnStreamRegistered(chat.Id, peerId, null, true, true, CancellationToken.None);
        var live = await liveBackend.GetState(chat.Id, CancellationToken.None);
        live.Should().NotBeNull();
        var v = live!.EffectiveVisibleStartLid;
        await liveBackend.UpdateSummary(chat.Id,
            new LiveSessionSummary {
                Title = "Latch", Description = "d", Summary = "s",
                EndEntryLid = v + 2, MessageCount = 3,
            }, CancellationToken.None);
        // The block needs the 3 summarised rows plus a full MinTailEntryCount tail below them, or the
        // tail floor caps the latched boundary before it can be observed.
        for (var i = 0; i < 3 + LiveFoldMath.MinTailEntryCount; i++)
            await Tester.CreateTextEntry(chat.Id, $"live {i}");

        // act
        var liveBlockUI = Tester.ScopedAppServices.GetRequiredService<LiveBlockUI>();
        var blockState = await liveBlockUI.GetBlockState(chat.Id, CancellationToken.None);

        // assert
        blockState.FoldBoundaryLid.Should().Be(v + 3);
        blockState.Overlay.Should().BeNull();
    }

    [Fact]
    public async Task LeaveKeepsRenderedTailVisible()
    {
        // Hanging up must freeze the render, not collapse it - a reader mid-scroll must not jump.

        // arrange
        await Tester.SignInAsUniqueBob();
        var chat = await CreateSettledChat("leave-freeze-test");
        var author = await Tester.GetOwnAuthor(chat.Id).Require();
        var peerId = AuthorId.New(chat.Id, 777_100);
        var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
        await liveBackend.OnStreamRegistered(chat.Id, author.Id, null, true, true, CancellationToken.None);
        await liveBackend.OnStreamRegistered(chat.Id, peerId, null, true, true, CancellationToken.None);
        var live = await liveBackend.GetState(chat.Id, CancellationToken.None);
        live.Should().NotBeNull();
        var v = live!.EffectiveVisibleStartLid;
        for (var i = 0; i < 3; i++)
            await Tester.CreateTextEntry(chat.Id, $"folded-{i}");
        await liveBackend.UpdateSummary(chat.Id,
            new LiveSessionSummary {
                Title = "Recap", Description = "d", Summary = "s",
                EndEntryLid = v + 2, MessageCount = 3,
            }, CancellationToken.None);
        for (var i = 0; i < 3; i++)
            await Tester.CreateTextEntry(chat.Id, $"tail-{i}");

        var chatAudioUI = Tester.ScopedAppServices.GetRequiredService<ChatAudioUI>();
        var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
        var liveBlockUI = Tester.ScopedAppServices.GetRequiredService<LiveBlockUI>();
        await chatAudioUI.SetListeningState(chat.Id, true);
        chatUI.SelectChatOnNavigation(chat.Id);
        var idRange = await Tester.Chats.GetIdRange(Tester.Session, chat.Id, CancellationToken.None);
        var query = new ChatDataQuery(idRange, -chatUI.HalfLoadLimit, chatUI.HalfLoadLimit);
        List<long> joinedLeafLids = null!;
        await ComputedTest.When(async ct => {
            var items = await chatUI.GetChatItems(chat.Id, query, 0, ct);
            items.Items.OfType<ExpandedConversationMessage>().Should().ContainSingle();
            joinedLeafLids = LeafEntryLids(items);
        }, TimeSpan.FromSeconds(10));

        // act
        await chatAudioUI.SetListeningState(chat.Id, false);
        InvalidateAmIInLiveConversation(chatAudioUI, chat.Id);

        // assert - wait for the governor to actually latch the leave overlay, not just for a render
        // that still coincidentally looks unchanged before the leave propagates
        await ComputedTest.When(async ct => {
            var blockState = await liveBlockUI.GetBlockState(chat.Id, ct);
            blockState.Overlay.Should().NotBeNull();
        }, TimeSpan.FromSeconds(10));
        await ComputedTest.When(async ct => {
            var items = await chatUI.GetChatItems(chat.Id, query, 0, ct);
            LeafEntryLids(items).Should().Equal(joinedLeafLids);
            var block = items.Items.OfType<ExpandedConversationMessage>().Should().ContainSingle().Subject;
            // Left-but-still-live still emits the sticky header - the card must keep suppressing its own
            // title (HasSplitHeader) or leaving reintroduces the duplicate title the header split fixed.
            block.Items.OfType<LiveConversationHeader>().Should().ContainSingle();
            block.Items.OfType<ConversationMessage>().Single().HasSplitHeader.Should().BeTrue();
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task JoinedLiveBlockEmitsSingleLiveFooterAsLastChild()
    {
        // A joined live block must close with exactly one live footer as its last child - the box the
        // tint fills and the sticky containing block bound. The regular ConversationFooter is never
        // emitted for the live block.

        // arrange
        await Tester.SignInAsUniqueBob();
        var chat = await CreateSettledChat("live-footer-test");
        var author = await Tester.GetOwnAuthor(chat.Id).Require();
        var peerId = AuthorId.New(chat.Id, 777_180);
        var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
        await liveBackend.OnStreamRegistered(chat.Id, author.Id, null, true, true, CancellationToken.None);
        await liveBackend.OnStreamRegistered(chat.Id, peerId, null, true, true, CancellationToken.None);
        var live = await liveBackend.GetState(chat.Id, CancellationToken.None);
        live.Should().NotBeNull();
        var v = live!.EffectiveVisibleStartLid;
        for (var i = 0; i < 3; i++)
            await Tester.CreateTextEntry(chat.Id, $"folded-{i}");
        await liveBackend.UpdateSummary(chat.Id,
            new LiveSessionSummary {
                Title = "Recap", Description = "d", Summary = "s",
                EndEntryLid = v + 2, MessageCount = 3,
            }, CancellationToken.None);
        for (var i = 0; i < 3; i++)
            await Tester.CreateTextEntry(chat.Id, $"tail-{i}");

        var chatAudioUI = Tester.ScopedAppServices.GetRequiredService<ChatAudioUI>();
        var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
        await chatAudioUI.SetListeningState(chat.Id, true);
        chatUI.SelectChatOnNavigation(chat.Id);
        var idRange = await Tester.Chats.GetIdRange(Tester.Session, chat.Id, CancellationToken.None);
        var query = new ChatDataQuery(idRange, -chatUI.HalfLoadLimit, chatUI.HalfLoadLimit);

        // act + assert
        await ComputedTest.When(async ct => {
            var items = await chatUI.GetChatItems(chat.Id, query, 0, ct);
            var block = items.Items.OfType<ExpandedConversationMessage>().Single();
            block.Items.OfType<LiveConversationFooter>().Should().ContainSingle();
            block.Items[^1].Should().BeOfType<LiveConversationFooter>();
            items.Items.SelectMany(i => i.GetLeafMessages())
                .OfType<ConversationFooter>().Should().BeEmpty();
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task JoinedLiveBlockSplitsIntoStickyHeaderAndDescription()
    {
        // The live card splits into a sticky title-band item (rendered like ConversationHeader) plus a
        // scrollable description item - so a joiner's summary scrolls out while the title stays pinned.

        // arrange - same joined-block setup with a landed title
        await Tester.SignInAsUniqueBob();
        var chat = await CreateSettledChat("live-split-test");
        var author = await Tester.GetOwnAuthor(chat.Id).Require();
        var peerId = AuthorId.New(chat.Id, 777_190);
        var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
        await liveBackend.OnStreamRegistered(chat.Id, author.Id, null, true, true, CancellationToken.None);
        await liveBackend.OnStreamRegistered(chat.Id, peerId, null, true, true, CancellationToken.None);
        var live = await liveBackend.GetState(chat.Id, CancellationToken.None);
        var v = live!.EffectiveVisibleStartLid;
        for (var i = 0; i < 3; i++)
            await Tester.CreateTextEntry(chat.Id, $"folded-{i}");
        await liveBackend.UpdateSummary(chat.Id,
            new LiveSessionSummary {
                Title = "Recap", Description = "d", Summary = "s",
                EndEntryLid = v + 2, MessageCount = 3,
            }, CancellationToken.None);

        var chatAudioUI = Tester.ScopedAppServices.GetRequiredService<ChatAudioUI>();
        var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
        await chatAudioUI.SetListeningState(chat.Id, true);
        chatUI.SelectChatOnNavigation(chat.Id);
        var idRange = await Tester.Chats.GetIdRange(Tester.Session, chat.Id, CancellationToken.None);
        var query = new ChatDataQuery(idRange, -chatUI.HalfLoadLimit, chatUI.HalfLoadLimit);

        // assert - the block leads with a header item then the description card, in that order
        await ComputedTest.When(async ct => {
            var items = await chatUI.GetChatItems(chat.Id, query, 0, ct);
            var block = items.Items.OfType<ExpandedConversationMessage>().Single();
            block.Items.OfType<LiveConversationHeader>().Should().ContainSingle();
            var headerIndex = block.Items.ToList().FindIndex(i => i is LiveConversationHeader);
            var cardIndex = block.Items.ToList().FindIndex(i => i is ConversationMessage);
            headerIndex.Should().BeGreaterThanOrEqualTo(0);
            cardIndex.Should().BeGreaterThan(headerIndex, "the scrollable description card follows the sticky header");
            // HasSplitHeader is the gating signal the card uses to suppress its own title - it must be set
            // whenever a LiveConversationHeader was emitted for this card, or the card renders a duplicate title.
            block.Items.OfType<ConversationMessage>().Single().HasSplitHeader.Should().BeTrue();
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task NewSpokenEntriesAfterLeaveStayHiddenButTypedOnesSurface()
    {
        // A collapsed block hides what the call produces - a transcribed entry landing after hang-up
        // can't sneak in - and leaving changes none of that. It must not hide what the viewer types:
        // the hidden tail runs to long.MaxValue, so hiding by lid alone also swallowed typed messages,
        // and the sender never saw their own message.

        // arrange
        await Tester.SignInAsUniqueBob();
        var chat = await CreateSettledChat("leave-freeze-new-entries-test");
        var author = await Tester.GetOwnAuthor(chat.Id).Require();
        var peerId = AuthorId.New(chat.Id, 777_110);
        var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
        await liveBackend.OnStreamRegistered(chat.Id, author.Id, null, true, true, CancellationToken.None);
        await liveBackend.OnStreamRegistered(chat.Id, peerId, null, true, true, CancellationToken.None);
        var live = await liveBackend.GetState(chat.Id, CancellationToken.None);
        live.Should().NotBeNull();
        var v = live!.EffectiveVisibleStartLid;
        for (var i = 0; i < 3; i++)
            await Tester.CreateTextEntry(chat.Id, $"folded-{i}");
        await liveBackend.UpdateSummary(chat.Id,
            new LiveSessionSummary {
                Title = "Recap", Description = "d", Summary = "s",
                EndEntryLid = v + 2, MessageCount = 3,
            }, CancellationToken.None);
        for (var i = 0; i < 3; i++)
            await Tester.CreateTextEntry(chat.Id, $"tail-{i}");

        var chatAudioUI = Tester.ScopedAppServices.GetRequiredService<ChatAudioUI>();
        var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
        await chatAudioUI.SetListeningState(chat.Id, true);
        chatUI.SelectChatOnNavigation(chat.Id);
        await CollapseJoinedLiveBlock(chatUI, chat.Id, live.ToConversation());
        var idRange = await Tester.Chats.GetIdRange(Tester.Session, chat.Id, CancellationToken.None);
        var query = new ChatDataQuery(idRange, -chatUI.HalfLoadLimit, chatUI.HalfLoadLimit);
        await ComputedTest.When(async ct => {
            var items = await chatUI.GetChatItems(chat.Id, query, 0, ct);
            items.Items.OfType<ExpandedConversationMessage>().Should().ContainSingle();
        }, TimeSpan.FromSeconds(10));
        await chatAudioUI.SetListeningState(chat.Id, false);
        InvalidateAmIInLiveConversation(chatAudioUI, chat.Id);
        var liveBlockUI = Tester.ScopedAppServices.GetRequiredService<LiveBlockUI>();
        // Wait for the governor to actually latch the leave overlay - a render that still
        // coincidentally looks unchanged before the leave propagates would snapshot too early.
        await ComputedTest.When(async ct => {
            var blockState = await liveBlockUI.GetBlockState(chat.Id, ct);
            blockState.Overlay.Should().NotBeNull();
        }, TimeSpan.FromSeconds(10));
        List<long> frozenLeafLids = null!;
        await ComputedTest.When(async ct => {
            var items = await chatUI.GetChatItems(chat.Id, query, 0, ct);
            items.Items.OfType<ExpandedConversationMessage>().Should().ContainSingle();
            frozenLeafLids = LeafEntryLids(items);
        }, TimeSpan.FromSeconds(10));

        // act
        var spoken = await CreateSpokenEntry(chat.Id, "spoken after leave");
        // Enough typed entries to run well past an id-tile boundary (the first layer holds 5). A single
        // entry lands in a tile that's loaded for other reasons, so it can't catch a whole tile being
        // excluded from the load - which is exactly how the outer id-tile selection used to hide these.
        var typed = new List<ChatEntry>();
        for (var i = 0; i < 12; i++)
            typed.Add(await Tester.CreateTextEntry(chat.Id, $"typed after leave {i}"));

        // assert (sustained - the spoken entry never surfaces, every typed one does)
        await Task.Delay(1000);
        var items2 = await chatUI.GetChatItems(chat.Id, query, 0, CancellationToken.None);
        var lids = LeafEntryLids(items2);
        lids.Should().NotContain(spoken.Id.LocalId);
        lids.Should().Contain(typed.Select(e => e.Id.LocalId));
        lids.Should().Equal([..frozenLeafLids, ..typed.Select(e => e.Id.LocalId)]);
    }

    [Fact]
    public async Task CloseKeepsRenderedItemsAndKey()
    {
        // Closing the call must not disturb a viewer who was watching it live - same rows, same @key.

        // arrange
        await Tester.SignInAsUniqueBob();
        var chat = await CreateSettledChat("close-freeze-test");
        var author = await Tester.GetOwnAuthor(chat.Id).Require();
        var peerId = AuthorId.New(chat.Id, 777_120);
        var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
        await liveBackend.OnStreamRegistered(chat.Id, author.Id, null, true, true, CancellationToken.None);
        await liveBackend.OnStreamRegistered(chat.Id, peerId, null, true, true, CancellationToken.None);
        var live = await liveBackend.GetState(chat.Id, CancellationToken.None);
        live.Should().NotBeNull();
        var v = live!.EffectiveVisibleStartLid;
        for (var i = 0; i < 3; i++)
            await Tester.CreateTextEntry(chat.Id, $"folded-{i}");
        await liveBackend.UpdateSummary(chat.Id,
            new LiveSessionSummary {
                Title = "Recap", Description = "d", Summary = "s",
                EndEntryLid = v + 2, MessageCount = 3, IsExpandedByDefault = false,
            }, CancellationToken.None);
        for (var i = 0; i < 3; i++)
            await Tester.CreateTextEntry(chat.Id, $"tail-{i}");

        var chatAudioUI = Tester.ScopedAppServices.GetRequiredService<ChatAudioUI>();
        var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
        await chatAudioUI.SetListeningState(chat.Id, true);
        chatUI.SelectChatOnNavigation(chat.Id);
        var idRange = await Tester.Chats.GetIdRange(Tester.Session, chat.Id, CancellationToken.None);
        var query = new ChatDataQuery(idRange, -chatUI.HalfLoadLimit, chatUI.HalfLoadLimit);
        List<long> frozenLeafLids = null!;
        string frozenRenderKey = null!;
        await ComputedTest.When(async ct => {
            var items = await chatUI.GetChatItems(chat.Id, query, 0, ct);
            var block = items.Items.OfType<ExpandedConversationMessage>().Single();
            frozenLeafLids = LeafEntryLids(items);
            frozenRenderKey = ((IVirtualListItem)block).RenderKey;
        }, TimeSpan.FromSeconds(10));
        // The render key is keyed by V (the live-era render id), not by whatever the persisted
        // conversation ends up starting at - confirm it's the actual ConversationBlock-at-V format,
        // not just some captured-before value that happens to stay stable.
        frozenRenderKey.Should().Be(ChatMessageKey.New(ChatMessageKind.ConversationBlock, v).Value);

        // The two stream participants hang up - this only marks the session closing.
        await liveBackend.SetParticipation(chat.Id, peerId, ParticipationKind.Record, false, CancellationToken.None);
        await liveBackend.SetParticipation(chat.Id, author.Id, ParticipationKind.Record, false, CancellationToken.None);

        // act
        await liveBackend.FinalizeSession(chat.Id, CancellationToken.None);

        // assert
        await ComputedTest.When(async ct => {
            var liveState = await liveBackend.GetState(chat.Id, ct);
            liveState.Should().BeNull();
            var items = await chatUI.GetChatItems(chat.Id, query, 0, ct);
            LeafEntryLids(items).Should().Equal(frozenLeafLids);
            var block = items.Items.OfType<ExpandedConversationMessage>().Single();
            ((IVirtualListItem)block).RenderKey.Should().Be(frozenRenderKey);
            // Rows and @key are unchanged, but a completed session drops the live header - the card
            // falls back to its own regular header (HasSplitHeader == false). See
            // ClosedBlockRendersAsCompletedNotLive for the full completed-render assertions.
            block.Items.OfType<LiveConversationHeader>().Should().BeEmpty();
            block.Items.OfType<ConversationMessage>().Single().HasSplitHeader.Should().BeFalse();
        }, TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task CloseWithPreLatchContextKeepsFrozenTailVisible()
    {
        // When the summary's context reaches back before V, the persisted (materialized) conversation
        // starts at ContextStartLid, not V - id-tile loading must resolve that identity to the same
        // governed fold range GetTile uses, or the block's frozen (unfolded) tail loses its id-tiles
        // and goes missing after close. A second summary widens the raw range past the governed fold
        // boundary (which never advances without a viewport signal) so the fold and the persisted
        // range genuinely diverge - the exact condition that exposed the bug.

        // arrange
        await Tester.SignInAsUniqueBob();
        var chat = await CreateSettledChat("close-context-test");
        var context0 = await Tester.CreateTextEntry(chat.Id, "context-0");
        await Tester.CreateTextEntry(chat.Id, "context-1");
        var author = await Tester.GetOwnAuthor(chat.Id).Require();
        var peerId = AuthorId.New(chat.Id, 777_160);
        var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
        await liveBackend.OnStreamRegistered(chat.Id, author.Id, null, true, true, CancellationToken.None);
        await liveBackend.OnStreamRegistered(chat.Id, peerId, null, true, true, CancellationToken.None);
        var live = await liveBackend.GetState(chat.Id, CancellationToken.None);
        live.Should().NotBeNull();
        var v = live!.EffectiveVisibleStartLid;
        for (var i = 0; i < 3; i++)
            await Tester.CreateTextEntry(chat.Id, $"folded-{i}");
        await liveBackend.UpdateSummary(chat.Id,
            new LiveSessionSummary {
                Title = "Recap", Description = "d", Summary = "s",
                EndEntryLid = v + 2, MessageCount = 3, IsExpandedByDefault = false,
            }, CancellationToken.None);

        var chatAudioUI = Tester.ScopedAppServices.GetRequiredService<ChatAudioUI>();
        var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
        await chatAudioUI.SetListeningState(chat.Id, true);
        chatUI.SelectChatOnNavigation(chat.Id);
        var idRange = await Tester.Chats.GetIdRange(Tester.Session, chat.Id, CancellationToken.None);
        var query = new ChatDataQuery(idRange, -chatUI.HalfLoadLimit, chatUI.HalfLoadLimit);
        await ComputedTest.When(async ct => {
            var items = await chatUI.GetChatItems(chat.Id, query, 0, ct);
            items.Items.OfType<ExpandedConversationMessage>().Should().ContainSingle();
        }, TimeSpan.FromSeconds(10));

        for (var i = 0; i < 3; i++)
            await Tester.CreateTextEntry(chat.Id, $"tail-{i}");
        List<long> preCloseLeafLids = null!;
        await ComputedTest.When(async ct => {
            var items = await chatUI.GetChatItems(chat.Id, query, 0, ct);
            var lids = LeafEntryLids(items);
            lids.Should().Contain(v + 3);
            lids.Should().Contain(v + 4);
            lids.Should().Contain(v + 5);
            preCloseLeafLids = lids;
        }, TimeSpan.FromSeconds(10));
        preCloseLeafLids.Should().Contain(context0.LocalId, "the pre-latch context entry must already be visible");

        // A second summary widens EndEntryLid to cover the tail, but the governed fold boundary never
        // advances past its initial latch without a viewport signal, so it stays parked at V+3 - at
        // close, the fold range [V, V+3) is genuinely narrower than the persisted range
        // [ContextStartLid, V+6).
        await liveBackend.UpdateSummary(chat.Id,
            new LiveSessionSummary {
                Title = "Recap", Description = "d2", Summary = "s2",
                EndEntryLid = v + 5, MessageCount = 6, IsExpandedByDefault = false,
            }, CancellationToken.None);

        // The context reaches back to the first pre-latch entry - before FinalizeSession so it lands
        // on the materialized conversation (see LiveSessionsTest's finalize choreography).
        await liveBackend.SetContextStart(chat.Id, context0.LocalId, CancellationToken.None);
        await liveBackend.SetParticipation(chat.Id, peerId, ParticipationKind.Record, false, CancellationToken.None);
        await liveBackend.SetParticipation(chat.Id, author.Id, ParticipationKind.Record, false, CancellationToken.None);

        // act
        await liveBackend.FinalizeSession(chat.Id, CancellationToken.None);

        // assert - same leaf lids (context + fold + tail), one block, same live-era render key
        await ComputedTest.When(async ct => {
            var liveState = await liveBackend.GetState(chat.Id, ct);
            liveState.Should().BeNull();
            var items = await chatUI.GetChatItems(chat.Id, query, 0, ct);
            LeafEntryLids(items).Should().Equal(preCloseLeafLids);
            var block = items.Items.OfType<ExpandedConversationMessage>().Single();
            var expectedRenderKey = ChatMessageKey.New(ChatMessageKind.ConversationBlock, v).Value;
            ((IVirtualListItem)block).RenderKey.Should().Be(expectedRenderKey);
        }, TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task ToggleAfterCloseCollapsesBlock()
    {
        // Once closed, the reader can still manually collapse the frozen block - it just dismisses
        // the overlay rather than acting as an ordinary expand/collapse toggle.

        // arrange
        await Tester.SignInAsUniqueBob();
        var chat = await CreateSettledChat("close-toggle-test");
        var author = await Tester.GetOwnAuthor(chat.Id).Require();
        var peerId = AuthorId.New(chat.Id, 777_130);
        var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
        await liveBackend.OnStreamRegistered(chat.Id, author.Id, null, true, true, CancellationToken.None);
        await liveBackend.OnStreamRegistered(chat.Id, peerId, null, true, true, CancellationToken.None);
        var live = await liveBackend.GetState(chat.Id, CancellationToken.None);
        live.Should().NotBeNull();
        var v = live!.EffectiveVisibleStartLid;
        for (var i = 0; i < 3; i++)
            await Tester.CreateTextEntry(chat.Id, $"folded-{i}");
        await liveBackend.UpdateSummary(chat.Id,
            new LiveSessionSummary {
                Title = "Recap", Description = "d", Summary = "s",
                EndEntryLid = v + 2, MessageCount = 3, IsExpandedByDefault = false,
            }, CancellationToken.None);
        for (var i = 0; i < 3; i++)
            await Tester.CreateTextEntry(chat.Id, $"tail-{i}");

        var chatAudioUI = Tester.ScopedAppServices.GetRequiredService<ChatAudioUI>();
        var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
        await chatAudioUI.SetListeningState(chat.Id, true);
        chatUI.SelectChatOnNavigation(chat.Id);
        var idRange = await Tester.Chats.GetIdRange(Tester.Session, chat.Id, CancellationToken.None);
        var query = new ChatDataQuery(idRange, -chatUI.HalfLoadLimit, chatUI.HalfLoadLimit);
        await ComputedTest.When(async ct => {
            var items = await chatUI.GetChatItems(chat.Id, query, 0, ct);
            items.Items.OfType<ExpandedConversationMessage>().Should().ContainSingle();
        }, TimeSpan.FromSeconds(10));
        await liveBackend.SetParticipation(chat.Id, peerId, ParticipationKind.Record, false, CancellationToken.None);
        await liveBackend.SetParticipation(chat.Id, author.Id, ParticipationKind.Record, false, CancellationToken.None);
        await liveBackend.FinalizeSession(chat.Id, CancellationToken.None);
        ConversationId blockConversationId = null!;
        await ComputedTest.When(async ct => {
            var liveState = await liveBackend.GetState(chat.Id, ct);
            liveState.Should().BeNull();
            var items = await chatUI.GetChatItems(chat.Id, query, 0, ct);
            var block = items.Items.OfType<ExpandedConversationMessage>().Single();
            blockConversationId = block.Conversation!.Id;
        }, TimeSpan.FromSeconds(15));

        // act
        chatUI.ToggleExpandConversation(blockConversationId);

        // assert
        await ComputedTest.When(async ct => {
            var items = await chatUI.GetChatItems(chat.Id, query, 0, ct);
            items.Items.OfType<ExpandedConversationMessage>().Should().BeEmpty();
            items.Items.OfType<ConversationMessage>().Should().ContainSingle();
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task ToggleAfterOverlayDismissExpandsBlockAgain()
    {
        // The first toggle on a closed block dismisses the frozen overlay; every toggle after that
        // must act as an ordinary expand/collapse - a dead expand button here is a regression.

        // arrange
        await Tester.SignInAsUniqueBob();
        var chat = await CreateSettledChat("close-retoggle-test");
        var author = await Tester.GetOwnAuthor(chat.Id).Require();
        var peerId = AuthorId.New(chat.Id, 777_135);
        var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
        await liveBackend.OnStreamRegistered(chat.Id, author.Id, null, true, true, CancellationToken.None);
        await liveBackend.OnStreamRegistered(chat.Id, peerId, null, true, true, CancellationToken.None);
        var live = await liveBackend.GetState(chat.Id, CancellationToken.None);
        live.Should().NotBeNull();
        var v = live!.EffectiveVisibleStartLid;
        for (var i = 0; i < 3; i++)
            await Tester.CreateTextEntry(chat.Id, $"folded-{i}");
        await liveBackend.UpdateSummary(chat.Id,
            new LiveSessionSummary {
                Title = "Recap", Description = "d", Summary = "s",
                EndEntryLid = v + 2, MessageCount = 3, IsExpandedByDefault = false,
            }, CancellationToken.None);
        for (var i = 0; i < 3; i++)
            await Tester.CreateTextEntry(chat.Id, $"tail-{i}");

        var chatAudioUI = Tester.ScopedAppServices.GetRequiredService<ChatAudioUI>();
        var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
        await chatAudioUI.SetListeningState(chat.Id, true);
        chatUI.SelectChatOnNavigation(chat.Id);
        var idRange = await Tester.Chats.GetIdRange(Tester.Session, chat.Id, CancellationToken.None);
        var query = new ChatDataQuery(idRange, -chatUI.HalfLoadLimit, chatUI.HalfLoadLimit);
        await ComputedTest.When(async ct => {
            var items = await chatUI.GetChatItems(chat.Id, query, 0, ct);
            items.Items.OfType<ExpandedConversationMessage>().Should().ContainSingle();
        }, TimeSpan.FromSeconds(10));
        await liveBackend.SetParticipation(chat.Id, peerId, ParticipationKind.Record, false, CancellationToken.None);
        await liveBackend.SetParticipation(chat.Id, author.Id, ParticipationKind.Record, false, CancellationToken.None);
        await liveBackend.FinalizeSession(chat.Id, CancellationToken.None);
        ConversationId blockConversationId = null!;
        await ComputedTest.When(async ct => {
            var liveState = await liveBackend.GetState(chat.Id, ct);
            liveState.Should().BeNull();
            var items = await chatUI.GetChatItems(chat.Id, query, 0, ct);
            var block = items.Items.OfType<ExpandedConversationMessage>().Single();
            blockConversationId = block.Conversation!.Id;
        }, TimeSpan.FromSeconds(15));

        // act - first toggle dismisses the overlay and collapses the materialized block
        chatUI.ToggleExpandConversation(blockConversationId);
        ConversationId materializedId = null!;
        await ComputedTest.When(async ct => {
            var items = await chatUI.GetChatItems(chat.Id, query, 0, ct);
            items.Items.OfType<ExpandedConversationMessage>().Should().BeEmpty();
            var collapsed = items.Items.OfType<ConversationMessage>().Single();
            materializedId = collapsed.Conversation!.Id;
        }, TimeSpan.FromSeconds(10));

        // act - second toggle must expand the collapsed conversation
        chatUI.ToggleExpandConversation(materializedId);

        // assert
        await ComputedTest.When(async ct => {
            var items = await chatUI.GetChatItems(chat.Id, query, 0, ct);
            items.Items.OfType<ExpandedConversationMessage>().Should().ContainSingle();
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task ViewportGuardDefersFoldWhileVisible()
    {
        // The fold must defer while its entries are the topmost visible ones - even once a summary
        // pass widens the live pipeline's coverage over them - and complete once the reader scrolls
        // further so a later message becomes the topmost visible one. The boundary tracks the
        // viewport top, not the summary, so a visible entry can't vanish out from under the reader.

        // arrange
        await Tester.SignInAsUniqueBob();
        var chat = await CreateSettledChat("viewport-guard-test");
        var author = await Tester.GetOwnAuthor(chat.Id).Require();
        var peerId = AuthorId.New(chat.Id, 777_150);
        var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
        await liveBackend.OnStreamRegistered(chat.Id, author.Id, null, true, true, CancellationToken.None);
        await liveBackend.OnStreamRegistered(chat.Id, peerId, null, true, true, CancellationToken.None);
        var live = await liveBackend.GetState(chat.Id, CancellationToken.None);
        live.Should().NotBeNull();
        var v = live!.EffectiveVisibleStartLid;
        for (var i = 0; i < 3; i++)
            await Tester.CreateTextEntry(chat.Id, $"folded-{i}");
        await liveBackend.UpdateSummary(chat.Id,
            new LiveSessionSummary {
                Title = "Latch", Description = "d", Summary = "s",
                EndEntryLid = v + 2, MessageCount = 3,
            }, CancellationToken.None);
        await Tester.CreateTextEntry(chat.Id, "extra-1");
        await Tester.CreateTextEntry(chat.Id, "extra-2");

        var chatAudioUI = Tester.ScopedAppServices.GetRequiredService<ChatAudioUI>();
        var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
        await chatAudioUI.SetListeningState(chat.Id, true);
        chatUI.SelectChatOnNavigation(chat.Id);
        var idRange = await Tester.Chats.GetIdRange(Tester.Session, chat.Id, CancellationToken.None);
        var query = new ChatDataQuery(idRange, -chatUI.HalfLoadLimit, chatUI.HalfLoadLimit);
        chatUI.SetItemVisibility(new ChatViewItemVisibility(
            chat.Id,
            new HashSet<ChatMessageKey> {
                ChatMessageKey.New(ChatMessageKind.None, v + 3),
                ChatMessageKey.New(ChatMessageKind.None, v + 4),
            },
            false,
            false));
        await CollapseJoinedLiveBlock(chatUI, chat.Id, live.ToConversation());
        // Baseline taken once collapsed, so act 1 is compared against the state it actually acts on -
        // and v+3/v+4 being here at all is the guard already holding the fold at the viewport top.
        List<long> beforeLids = null!;
        await ComputedTest.When(async ct => {
            var items = await chatUI.GetChatItems(chat.Id, query, 0, ct);
            var lids = LeafEntryLids(items);
            lids.Should().Contain(v + 3);
            lids.Should().Contain(v + 4);
            beforeLids = lids;
        }, TimeSpan.FromSeconds(10));

        // act 1 - a summary pass widens the live pipeline's known coverage over v+3/v+4, but they're
        // still the topmost visible entries, so the viewport guard holds the fold there regardless
        await liveBackend.UpdateSummary(chat.Id,
            new LiveSessionSummary {
                Title = "Latch", Description = "d2", Summary = "s2",
                EndEntryLid = v + 4, MessageCount = 5,
            }, CancellationToken.None);
        await Task.Delay(700);
        LeafEntryLids(await chatUI.GetChatItems(chat.Id, query, 0, CancellationToken.None)).Should().Equal(beforeLids);

        // act 2 - the reader scrolls further: a later message becomes the topmost visible one, with a
        // full MinTailEntryCount tail below it so the floor doesn't hold the fold back on its own
        var tailEntry = await Tester.CreateTextEntry(chat.Id, "tail-3");
        for (var i = 4; i < 3 + LiveFoldMath.MinTailEntryCount; i++)
            await Tester.CreateTextEntry(chat.Id, $"tail-{i}");
        chatUI.SetItemVisibility(new ChatViewItemVisibility(
            chat.Id,
            new HashSet<ChatMessageKey> { ChatMessageKey.New(ChatMessageKind.None, tailEntry.LocalId) },
            false,
            false));

        // assert - the boundary advances past v+3/v+4, folding them
        await ComputedTest.When(async ct => {
            var lids = LeafEntryLids(await chatUI.GetChatItems(chat.Id, query, 0, ct));
            lids.Should().NotContain(v + 3);
            lids.Should().NotContain(v + 4);
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task LeaveFreezesReactivelyWithoutWaitingForGovernor()
    {
        // Determinism: the freeze must be a reactive function of "am I still attending this block",
        // not an async governor write that lands a beat later - otherwise a hang-up can flash a
        // collapsed frame before the overlay latches. Prove GetBlockState goes stale from the leave
        // signal alone, before the governor has had a turn to write anything.

        // arrange
        await Tester.SignInAsUniqueBob();
        var chat = await CreateSettledChat("leave-reactive-test");
        var author = await Tester.GetOwnAuthor(chat.Id).Require();
        var peerId = AuthorId.New(chat.Id, 777_170);
        var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
        await liveBackend.OnStreamRegistered(chat.Id, author.Id, null, true, true, CancellationToken.None);
        await liveBackend.OnStreamRegistered(chat.Id, peerId, null, true, true, CancellationToken.None);
        var live = await liveBackend.GetState(chat.Id, CancellationToken.None);
        live.Should().NotBeNull();
        var v = live!.EffectiveVisibleStartLid;
        for (var i = 0; i < 3; i++)
            await Tester.CreateTextEntry(chat.Id, $"folded-{i}");
        await liveBackend.UpdateSummary(chat.Id,
            new LiveSessionSummary {
                Title = "Recap", Description = "d", Summary = "s",
                EndEntryLid = v + 2, MessageCount = 3,
            }, CancellationToken.None);
        for (var i = 0; i < 3; i++)
            await Tester.CreateTextEntry(chat.Id, $"tail-{i}");

        var chatAudioUI = Tester.ScopedAppServices.GetRequiredService<ChatAudioUI>();
        var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
        var liveBlockUI = Tester.ScopedAppServices.GetRequiredService<LiveBlockUI>();
        await chatAudioUI.SetListeningState(chat.Id, true);
        chatUI.SelectChatOnNavigation(chat.Id);
        // Let the governor latch the joined state and the attending latch first.
        await ComputedTest.When(async ct => {
            var s = await liveBlockUI.GetBlockState(chat.Id, ct);
            s.Overlay.Should().BeNull();
            s.WasAttending.Should().BeTrue();
        }, TimeSpan.FromSeconds(10));

        var computed = await Computed.Capture(() => liveBlockUI.GetBlockState(chat.Id, CancellationToken.None));
        computed.Value.Overlay.Should().BeNull();

        // act - hang up, then invalidate the leave signal source synchronously. The governor loop
        // reacts to the same invalidation, but only asynchronously; we do NOT yield to it before the
        // assertion below, so nothing it writes can account for the staleness.
        await chatAudioUI.SetListeningState(chat.Id, false);
        using (Invalidation.Begin())
            _ = chatAudioUI.GetState(chat.Id);

        // assert - GetBlockState is already stale purely from the leave signal (it depends on it
        // reactively). Checked synchronously: the governor cannot have run yet. This is the actual
        // determinism guarantee - the freeze reacts to leaving directly, not via the governor's write.
        computed.IsConsistent().Should().BeFalse(
            "the freeze must react to leaving directly, not wait for the governor's async write");

        // and the recomputed state carries the freeze overlay (re-invalidate each poll to defeat the
        // non-reactive ChatAudioUI.GetState lag - see InvalidateAmIInLiveConversation).
        await ComputedTest.When(async ct => {
            InvalidateAmIInLiveConversation(chatAudioUI, chat.Id);
            var s = await liveBlockUI.GetBlockState(chat.Id, ct);
            s.Overlay.Should().NotBeNull();
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task ClosedBlockRendersAsCompletedNotLive()
    {
        // Once the session ends the frozen block must read as a completed conversation - no live
        // (animated) header, no live footer - even though it stays frozen under its live-era @key.

        // arrange
        await Tester.SignInAsUniqueBob();
        var chat = await CreateSettledChat("closed-completed-test");
        var author = await Tester.GetOwnAuthor(chat.Id).Require();
        var peerId = AuthorId.New(chat.Id, 777_200);
        var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
        await liveBackend.OnStreamRegistered(chat.Id, author.Id, null, true, true, CancellationToken.None);
        await liveBackend.OnStreamRegistered(chat.Id, peerId, null, true, true, CancellationToken.None);
        var live = await liveBackend.GetState(chat.Id, CancellationToken.None);
        var v = live!.EffectiveVisibleStartLid;
        for (var i = 0; i < 3; i++)
            await Tester.CreateTextEntry(chat.Id, $"folded-{i}");
        await liveBackend.UpdateSummary(chat.Id,
            new LiveSessionSummary {
                Title = "Recap", Description = "d", Summary = "s",
                EndEntryLid = v + 2, MessageCount = 3, IsExpandedByDefault = false,
            }, CancellationToken.None);
        for (var i = 0; i < 3; i++)
            await Tester.CreateTextEntry(chat.Id, $"tail-{i}");

        var chatAudioUI = Tester.ScopedAppServices.GetRequiredService<ChatAudioUI>();
        var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
        await chatAudioUI.SetListeningState(chat.Id, true);
        chatUI.SelectChatOnNavigation(chat.Id);
        var idRange = await Tester.Chats.GetIdRange(Tester.Session, chat.Id, CancellationToken.None);
        var query = new ChatDataQuery(idRange, -chatUI.HalfLoadLimit, chatUI.HalfLoadLimit);
        // while live the block leads with the live header
        await ComputedTest.When(async ct => {
            var items = await chatUI.GetChatItems(chat.Id, query, 0, ct);
            var block = items.Items.OfType<ExpandedConversationMessage>().Single();
            block.Items.OfType<LiveConversationHeader>().Should().ContainSingle();
        }, TimeSpan.FromSeconds(10));

        // act - both participants hang up and the session finalizes
        await liveBackend.SetParticipation(chat.Id, peerId, ParticipationKind.Record, false, CancellationToken.None);
        await liveBackend.SetParticipation(chat.Id, author.Id, ParticipationKind.Record, false, CancellationToken.None);
        await liveBackend.FinalizeSession(chat.Id, CancellationToken.None);

        // assert - the completed block drops the live header + footer; the card renders its own
        // regular header (HasSplitHeader == false)
        await ComputedTest.When(async ct => {
            var liveState = await liveBackend.GetState(chat.Id, ct);
            liveState.Should().BeNull();
            var items = await chatUI.GetChatItems(chat.Id, query, 0, ct);
            var block = items.Items.OfType<ExpandedConversationMessage>().Single();
            block.Items.OfType<LiveConversationHeader>()
                .Should().BeEmpty("a completed session must not render the live animated header");
            block.Items.OfType<LiveConversationFooter>()
                .Should().BeEmpty("a completed session must not render the live footer");
            block.Items.OfType<ConversationFooter>()
                .Should().ContainSingle("a completed block still closes with its own regular footer band");
            block.Items[^1].Should().BeOfType<ConversationFooter>(
                "the regular footer is the block's last child, past the frozen tail");
            var card = block.Items.OfType<ConversationMessage>().Single();
            card.HasSplitHeader.Should().BeFalse("the card renders its own regular header once completed");
        }, TimeSpan.FromSeconds(15));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClosedBlockNeverDropsItsFooter(bool mustLeaveBeforeClose)
    {
        // HasSplitFooter and the block's appended footer are decided from the same liveBlockId, so a
        // snapshot carrying a suppressed card and no footer for it means the two disagreed - the card
        // then renders with nothing below its description.

        // arrange - joined live session with a landed summary, card collapsed like the reported case
        await Tester.SignInAsUniqueBob();
        var chat = await CreateSettledChat($"footerless-card-test-{mustLeaveBeforeClose}");
        var author = await Tester.GetOwnAuthor(chat.Id).Require();
        var peerId = AuthorId.New(chat.Id, 777_320);
        var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
        await liveBackend.OnStreamRegistered(chat.Id, author.Id, null, true, true, CancellationToken.None);
        await liveBackend.OnStreamRegistered(chat.Id, peerId, null, true, true, CancellationToken.None);
        var live = await liveBackend.GetState(chat.Id, CancellationToken.None);
        live.Should().NotBeNull();
        var v = live!.EffectiveVisibleStartLid;
        for (var i = 0; i < 3; i++)
            await Tester.CreateTextEntry(chat.Id, $"folded-{i}");
        await liveBackend.UpdateSummary(chat.Id,
            new LiveSessionSummary {
                Title = "Recap", Description = "d", Summary = "s",
                EndEntryLid = v + 2, MessageCount = 3, IsExpandedByDefault = false,
            }, CancellationToken.None);
        for (var i = 0; i < 3; i++)
            await Tester.CreateTextEntry(chat.Id, $"tail-{i}");

        var chatAudioUI = Tester.ScopedAppServices.GetRequiredService<ChatAudioUI>();
        var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
        await chatAudioUI.SetListeningState(chat.Id, true);
        chatUI.SelectChatOnNavigation(chat.Id);
        var idRange = await Tester.Chats.GetIdRange(Tester.Session, chat.Id, CancellationToken.None);
        var query = new ChatDataQuery(idRange, -chatUI.HalfLoadLimit, chatUI.HalfLoadLimit);
        await ComputedTest.When(async ct => {
            var items = await chatUI.GetChatItems(chat.Id, query, 0, ct);
            items.Items.OfType<ExpandedConversationMessage>().Should().ContainSingle();
        }, TimeSpan.FromSeconds(10));

        using var cts = new CancellationTokenSource();
        var violations = new List<string>();
        var samplerTask = Task.Run(async () => {
            while (!cts.IsCancellationRequested) {
                var items = await chatUI.GetChatItems(chat.Id, query, 0, CancellationToken.None);
                var violation = FindFooterlessCard(items);
                if (violation != null && violations.Count < 5)
                    violations.Add(violation);
                await Task.Delay(TimeSpan.FromMilliseconds(20), CancellationToken.None);
            }
        }, CancellationToken.None);

        // act - the viewer either leaves first (freezing the block while the session runs on) or stays
        // to the end; either way both participants hang up and the session finalizes
        if (mustLeaveBeforeClose) {
            await chatAudioUI.SetListeningState(chat.Id, false);
            InvalidateAmIInLiveConversation(chatAudioUI, chat.Id);
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
        await liveBackend.SetParticipation(chat.Id, peerId, ParticipationKind.Record, false, CancellationToken.None);
        await liveBackend.SetParticipation(chat.Id, author.Id, ParticipationKind.Record, false, CancellationToken.None);
        await liveBackend.FinalizeSession(chat.Id, CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(2));
        if (!mustLeaveBeforeClose) {
            await chatAudioUI.SetListeningState(chat.Id, false);
            InvalidateAmIInLiveConversation(chatAudioUI, chat.Id);
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
        for (var i = 0; i < 3; i++)
            await Tester.CreateTextEntry(chat.Id, $"after-close-{i}");
        await Task.Delay(TimeSpan.FromSeconds(2));
        chatUI.ToggleExpandConversation(ConversationId.New(chat.Id, v));
        await Task.Delay(TimeSpan.FromSeconds(2));

        // assert
        cts.Cancel();
        await samplerTask;
        violations.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StopKeepsRenderKeysUnique(bool hasPreLatchContext)
    {
        // Every snapshot along the live -> ordinary conversation switch must stay @key-unique.

        // arrange
        await Tester.SignInAsUniqueBob();
        var chat = await CreateSettledChat($"stop-dup-key-test-{hasPreLatchContext}");
        var context0 = await Tester.CreateTextEntry(chat.Id, "context-0");
        await Tester.CreateTextEntry(chat.Id, "context-1");
        var author = await Tester.GetOwnAuthor(chat.Id).Require();
        var peerId = AuthorId.New(chat.Id, 777_240);
        var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
        await liveBackend.OnStreamRegistered(chat.Id, author.Id, null, true, true, CancellationToken.None);
        await liveBackend.OnStreamRegistered(chat.Id, peerId, null, true, true, CancellationToken.None);
        var live = await liveBackend.GetState(chat.Id, CancellationToken.None);
        live.Should().NotBeNull();
        var v = live!.EffectiveVisibleStartLid;
        var tileSize = (int)ChatUI.EntryIdTileLayer.TileSize;
        ChatEntry lastFolded = null!;
        for (var i = 0; i < tileSize * 3; i++)
            lastFolded = await Tester.CreateTextEntry(chat.Id, $"folded-{i}");
        await liveBackend.UpdateSummary(chat.Id,
            new LiveSessionSummary {
                Title = "Recap", Description = "d", Summary = "s",
                EndEntryLid = lastFolded.LocalId, MessageCount = tileSize * 3, IsExpandedByDefault = true,
            }, CancellationToken.None);
        for (var i = 0; i < tileSize; i++)
            await Tester.CreateTextEntry(chat.Id, $"tail-{i}");

        var chatAudioUI = Tester.ScopedAppServices.GetRequiredService<ChatAudioUI>();
        var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
        await chatAudioUI.SetListeningState(chat.Id, true);
        chatUI.SelectChatOnNavigation(chat.Id);
        var idRange = await Tester.Chats.GetIdRange(Tester.Session, chat.Id, CancellationToken.None);
        var query = new ChatDataQuery(idRange, -chatUI.HalfLoadLimit, chatUI.HalfLoadLimit);
        await ComputedTest.When(async ct => {
            var items = await chatUI.GetChatItems(chat.Id, query, 0, ct);
            items.Items.OfType<ExpandedConversationMessage>().Should().ContainSingle();
        }, TimeSpan.FromSeconds(10));

        using var cts = new CancellationTokenSource();
        var samplerTask = Task.Run(async () => {
            while (!cts.IsCancellationRequested) {
                var items = await chatUI.GetChatItems(chat.Id, query, 0, CancellationToken.None);
                AssertUniqueRenderKeys(items);
                await Task.Delay(TimeSpan.FromMilliseconds(20), CancellationToken.None);
            }
        }, CancellationToken.None);

        // act
        if (hasPreLatchContext)
            await liveBackend.SetContextStart(chat.Id, context0.LocalId, CancellationToken.None);
        await liveBackend.SetParticipation(chat.Id, peerId, ParticipationKind.Record, false, CancellationToken.None);
        await liveBackend.SetParticipation(chat.Id, author.Id, ParticipationKind.Record, false, CancellationToken.None);
        await liveBackend.FinalizeSession(chat.Id, CancellationToken.None);
        await chatAudioUI.SetListeningState(chat.Id, false);
        InvalidateAmIInLiveConversation(chatAudioUI, chat.Id);
        await Task.Delay(TimeSpan.FromSeconds(2));
        for (var i = 0; i < 3; i++)
            await Tester.CreateTextEntry(chat.Id, $"after-close-{i}");
        await Task.Delay(TimeSpan.FromSeconds(1));
        chatUI.ToggleExpandConversation(ConversationId.New(chat.Id, v));
        await Task.Delay(TimeSpan.FromSeconds(1));
        chatUI.ToggleExpandConversation(ConversationId.New(chat.Id,
            hasPreLatchContext ? context0.LocalId : v));

        // assert
        await Task.Delay(TimeSpan.FromSeconds(2));
        cts.Cancel();
        await samplerTask;
    }

    [Fact]
    public async Task ThreadInsideFrozenBlockKeepsSingleBlock()
    {
        // A thread start carries no Conversation, so inside a frozen block it is held by the block's
        // lid range alone - and that range stops at the frozen BlockEndLid while the conversation
        // itself keeps growing. Everything past the thread must stay in the same block.

        // arrange - joined live session with a landed summary
        await Tester.SignInAsUniqueBob();
        var chat = await CreateSettledChat("frozen-thread-split-test");
        var author = await Tester.GetOwnAuthor(chat.Id).Require();
        var peerId = AuthorId.New(chat.Id, 777_250);
        var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
        await liveBackend.OnStreamRegistered(chat.Id, author.Id, null, true, true, CancellationToken.None);
        await liveBackend.OnStreamRegistered(chat.Id, peerId, null, true, true, CancellationToken.None);
        var live = await liveBackend.GetState(chat.Id, CancellationToken.None);
        live.Should().NotBeNull();
        var v = live!.EffectiveVisibleStartLid;
        for (var i = 0; i < 3; i++)
            await Tester.CreateTextEntry(chat.Id, $"folded-{i}");
        await liveBackend.UpdateSummary(chat.Id,
            new LiveSessionSummary {
                Title = "Recap", Description = "d", Summary = "s",
                EndEntryLid = v + 2, MessageCount = 3, IsExpandedByDefault = false,
            }, CancellationToken.None);

        var chatAudioUI = Tester.ScopedAppServices.GetRequiredService<ChatAudioUI>();
        var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
        await chatAudioUI.SetListeningState(chat.Id, true);
        chatUI.SelectChatOnNavigation(chat.Id);
        var idRange = await Tester.Chats.GetIdRange(Tester.Session, chat.Id, CancellationToken.None);
        var query = new ChatDataQuery(idRange, -chatUI.HalfLoadLimit, chatUI.HalfLoadLimit);
        await ComputedTest.When(async ct => {
            var items = await chatUI.GetChatItems(chat.Id, query, 0, ct);
            items.Items.OfType<ExpandedConversationMessage>().Should().ContainSingle();
        }, TimeSpan.FromSeconds(10));

        // the tail the viewer sees before leaving: a thread start, then one more message
        await Tester.CreateTextEntry(chat.Id, "tail-0");
        var threadStart = await Tester.CreateTextEntry(chat.Id, "thread-start");
        var lastEntry = await Tester.CreateTextEntry(chat.Id, "tail-1");
        await Tester.Commander.Call(
            new ChatThreads_Start {
                Session = Tester.Session,
                ParentChatId = chat.Id,
                Title = "Thread",
                Description = "",
                EntryIds = [threadStart.Id],
            },
            CancellationToken.None);

        // act - leave (freezing BlockEndLid at the summarized end), then a later summary stretches
        // the conversation past that frozen end, and the session closes
        await chatAudioUI.SetListeningState(chat.Id, false);
        InvalidateAmIInLiveConversation(chatAudioUI, chat.Id);
        await liveBackend.UpdateSummary(chat.Id,
            new LiveSessionSummary {
                Title = "Recap", Description = "d2", Summary = "s2",
                EndEntryLid = lastEntry.LocalId, MessageCount = 6, IsExpandedByDefault = false,
            }, CancellationToken.None);
        await liveBackend.SetParticipation(chat.Id, peerId, ParticipationKind.Record, false, CancellationToken.None);
        await liveBackend.SetParticipation(chat.Id, author.Id, ParticipationKind.Record, false, CancellationToken.None);
        await liveBackend.FinalizeSession(chat.Id, CancellationToken.None);

        // assert
        await ComputedTest.When(async ct => {
            var liveState = await liveBackend.GetState(chat.Id, ct);
            liveState.Should().BeNull();
            var items = await chatUI.GetChatItems(chat.Id, query, 0, ct);
            AssertUniqueRenderKeys(items);
            var block = items.Items.OfType<ExpandedConversationMessage>()
                .Should().ContainSingle("a conversation renders as exactly one block").Subject;
            var thread = block.Items.OfType<ThreadMessage>()
                .Should().ContainSingle("the thread start stays inside the block").Subject;
            thread.Conversation!.Id.Should().Be(block.Conversation!.Id,
                "the thread carries its conversation, so it is held by id and not by lid alone");
        }, TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task ThreadOutsideTheRequestedRangeIsNotRendered()
    {
        // The first tile widens its request by one tile to learn the preceding entry; a thread start
        // in that widened part must not leak in as a leading item outside the requested range.

        // arrange - the thread opens a tile, and exactly one more tile follows it, so a tail-pinned
        // window loads that last tile and reaches the thread's tile only by widening
        await Tester.SignInAsUniqueBob();
        var chat = await CreateSettledChat("thread-widened-tile-test");
        var tileSize = (int)ChatUI.EntryIdTileLayer.TileSize;
        ChatEntry entry;
        do {
            entry = await Tester.CreateTextEntry(chat.Id, "filler");
        } while ((entry.LocalId + 1) % tileSize != 0);
        var threadStart = await Tester.CreateTextEntry(chat.Id, "thread-start");
        (threadStart.LocalId % tileSize).Should().Be(0, "the thread must open its own tile");
        await Tester.Commander.Call(
            new ChatThreads_Start {
                Session = Tester.Session,
                ParentChatId = chat.Id,
                Title = "Thread",
                Description = "",
                EntryIds = [threadStart.Id],
            },
            CancellationToken.None);
        ChatEntry lastEntry = null!;
        for (var i = 0; i < tileSize * 2 - 1; i++)
            lastEntry = await Tester.CreateTextEntry(chat.Id, $"entry-{i}");

        // act - a window pinned to the chat's tail, far enough down that the thread's tile is only
        // ever reachable as the widened part of the first loaded tile
        var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
        var tailRange = new Range<long>(lastEntry.LocalId, lastEntry.LocalId + 1);
        var query = new ChatDataQuery(tailRange, 0, 0);
        var items = await chatUI.GetChatItems(chat.Id, query, 0, CancellationToken.None);

        // assert
        AssertUniqueRenderKeys(items);
        var leaves = items.Items.SelectMany(i => i.GetLeafMessages()).ToList();
        LeafEntryLids(items).Min().Should().BeGreaterThan(threadStart.LocalId,
            "the loaded window must start past the thread or this test doesn't bite");
        leaves.OfType<ThreadMessage>()
            .Should().BeEmpty("a thread start below the loaded range must not be emitted");
    }

    [Fact]
    public async Task TierOneCloseDissolvesBeforeVanishing()
    {
        // A too-short (never-summarized) session leaves no card behind, but it must not vanish in one
        // frame - the block is held briefly as "dissolving" so it can fade + collapse out.

        // arrange - a joined live session that never gets a summary
        await Tester.SignInAsUniqueBob();
        var chat = await CreateSettledChat("tier1-dissolve-test");
        var author = await Tester.GetOwnAuthor(chat.Id).Require();
        var peerId = AuthorId.New(chat.Id, 777_210);
        var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
        await liveBackend.OnStreamRegistered(chat.Id, author.Id, null, true, true, CancellationToken.None);
        await liveBackend.OnStreamRegistered(chat.Id, peerId, null, true, true, CancellationToken.None);
        var live = await liveBackend.GetState(chat.Id, CancellationToken.None);
        live.Should().NotBeNull();
        for (var i = 0; i < 2; i++)
            await Tester.CreateTextEntry(chat.Id, $"entry-{i}");

        var chatAudioUI = Tester.ScopedAppServices.GetRequiredService<ChatAudioUI>();
        var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
        var liveBlockUI = Tester.ScopedAppServices.GetRequiredService<LiveBlockUI>();
        liveBlockUI.DissolveDuration = TimeSpan.FromSeconds(10);
        await chatAudioUI.SetListeningState(chat.Id, true);
        InvalidateAmIInLiveConversation(chatAudioUI, chat.Id);
        chatUI.SelectChatOnNavigation(chat.Id);
        // Let the governor latch WasAttending + the template (HadSummary == false).
        await ComputedTest.When(async ct => {
            var s = await liveBlockUI.GetBlockState(chat.Id, ct);
            s.WasAttending.Should().BeTrue();
        }, TimeSpan.FromSeconds(10));

        // act - tier-1 close (never summarized)
        await liveBackend.SetParticipation(chat.Id, peerId, ParticipationKind.Record, false, CancellationToken.None);
        await liveBackend.SetParticipation(chat.Id, author.Id, ParticipationKind.Record, false, CancellationToken.None);
        await liveBackend.FinalizeSession(chat.Id, CancellationToken.None);

        // assert - the block is held, dissolving, rather than dropping to null immediately
        await ComputedTest.When(async ct => {
            var s = await liveBlockUI.GetBlockState(chat.Id, ct);
            s.Overlay.Should().NotBeNull("a tier-1 close dissolves the block before removing it");
            s.Overlay!.IsDissolving.Should().BeTrue();
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task ExpandedLiveBlockHasNoDescription()
    {
        // Expanded reads as a plain conversation - the card's description box must not surface, and the
        // sticky header (LiveConversationHeaderView) never carries a message count in either state.

        // arrange
        await Tester.SignInAsUniqueBob();
        var chat = await CreateSettledChat("expanded-no-desc-test");
        var author = await Tester.GetOwnAuthor(chat.Id).Require();
        var peerId = AuthorId.New(chat.Id, 777_320);
        var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
        await liveBackend.OnStreamRegistered(chat.Id, author.Id, null, true, true, CancellationToken.None);
        await liveBackend.OnStreamRegistered(chat.Id, peerId, null, true, true, CancellationToken.None);
        var live = await liveBackend.GetState(chat.Id, CancellationToken.None);
        var v = live!.EffectiveVisibleStartLid;
        await liveBackend.UpdateSummary(chat.Id, new LiveSessionSummary {
            Title = "Recap", Description = "a description", Summary = "s", EndEntryLid = v, MessageCount = 1,
        }, CancellationToken.None);
        for (var i = 0; i < 3; i++)
            await Tester.CreateTextEntry(chat.Id, $"m-{i}");

        var chatAudioUI = Tester.ScopedAppServices.GetRequiredService<ChatAudioUI>();
        var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
        await chatAudioUI.SetListeningState(chat.Id, true);
        chatUI.SelectChatOnNavigation(chat.Id);
        // ensure the block renders expanded
        var live2 = await Tester.Conversations.Get(Tester.Session, live.ToConversation().Id, CancellationToken.None);
        if (live2 is { IsExpandedByDefault: false })
            chatUI.ToggleExpandConversation(live.ToConversation().Id);

        var idRange = await Tester.Chats.GetIdRange(Tester.Session, chat.Id, CancellationToken.None);
        var query = new ChatDataQuery(idRange, -chatUI.HalfLoadLimit, chatUI.HalfLoadLimit);
        await ComputedTest.When(async ct => {
            var items = await chatUI.GetChatItems(chat.Id, query, 0, ct);
            var block = items.Items.OfType<ExpandedConversationMessage>().Single();
            // HasSplitHeader gates the description box off (Task 3); the structural guard here is that
            // the block still renders as one split-header card once expanded.
            block.Items.OfType<ConversationMessage>().Single().HasSplitHeader.Should().BeTrue();
        }, TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task JoinFromBlockStartsRecording()
    {
        // arrange - a live session this Bob has not joined
        await Tester.SignInAsUniqueBob();
        var chat = await CreateSettledChat("join-records-test");
        var peerId = AuthorId.New(chat.Id, 777_310);
        var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
        await liveBackend.OnStreamRegistered(chat.Id, peerId, null, true, true, CancellationToken.None);
        var chatAudioUI = Tester.ScopedAppServices.GetRequiredService<ChatAudioUI>();
        var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
        chatUI.SelectChatOnNavigation(chat.Id);

        // act - the block's Join action
        await chatAudioUI.SetRecordingChatId(chat.Id);

        // assert - Bob is now recording in this chat
        await ComputedTest.When(async ct => {
            var s = await chatAudioUI.GetState(chat.Id).ConfigureAwait(true);
            s.IsRecording.Should().BeTrue("Join from the live block starts recording");
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task PreSummaryBlockShowsNoMetaCount()
    {
        // arrange
        await Tester.SignInAsUniqueBob();
        var chat = await CreateSettledChat("pre-summary-meta-test");
        var author = await Tester.GetOwnAuthor(chat.Id).Require();
        var peerId = AuthorId.New(chat.Id, 777_330);
        var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
        await liveBackend.OnStreamRegistered(chat.Id, author.Id, null, true, true, CancellationToken.None);
        await liveBackend.OnStreamRegistered(chat.Id, peerId, null, true, true, CancellationToken.None);
        var chatAudioUI = Tester.ScopedAppServices.GetRequiredService<ChatAudioUI>();
        var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
        await chatAudioUI.SetListeningState(chat.Id, true);
        chatUI.SelectChatOnNavigation(chat.Id);

        // act + assert - no UpdateSummary => no title => HasSummary must be false on the rendered live conversation
        var idRange = await Tester.Chats.GetIdRange(Tester.Session, chat.Id, CancellationToken.None);
        var query = new ChatDataQuery(idRange, -chatUI.HalfLoadLimit, chatUI.HalfLoadLimit);
        await ComputedTest.When(async ct => {
            var items = await chatUI.GetChatItems(chat.Id, query, 0, ct);
            var card = items.Items.SelectMany(i => i.GetLeafMessages())
                .OfType<ConversationMessage>().SingleOrDefault(c => c.Conversation!.Title.IsNullOrEmpty());
            card.Should().NotBeNull("a pre-summary live block still emits its card");
            // the card carries 0; the view must NOT render "0 messages"
            card!.Conversation!.MessageCount.Should().Be(0);
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task UnjoinedLiveBlockUsesStickyHeader()
    {
        await Tester.SignInAsUniqueBob();
        var chat = await CreateSettledChat("unjoined-shell-test");
        var peerId = AuthorId.New(chat.Id, 777_340);
        var peer2Id = AuthorId.New(chat.Id, 777_341);
        var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
        await liveBackend.OnStreamRegistered(chat.Id, peerId, null, true, true, CancellationToken.None);
        // 2+ => SessionStartedAt latches
        await liveBackend.OnStreamRegistered(chat.Id, peer2Id, null, true, true, CancellationToken.None);
        var live = await liveBackend.GetState(chat.Id, CancellationToken.None);
        var v = live!.EffectiveVisibleStartLid;
        await liveBackend.UpdateSummary(chat.Id, new LiveSessionSummary {
            Title = "Recap", Description = "d", Summary = "s", EndEntryLid = v, MessageCount = 1,
        }, CancellationToken.None);

        var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
        chatUI.SelectChatOnNavigation(chat.Id);   // Bob is NOT recording/listening => not joined
        var idRange = await Tester.Chats.GetIdRange(Tester.Session, chat.Id, CancellationToken.None);
        var query = new ChatDataQuery(idRange, -chatUI.HalfLoadLimit, chatUI.HalfLoadLimit);

        await ComputedTest.When(async ct => {
            var items = await chatUI.GetChatItems(chat.Id, query, 0, ct);
            items.Items.SelectMany(i => i.GetLeafMessages())
                .OfType<LiveConversationHeader>().Should().ContainSingle(
                    "the unjoined live block shares the sticky-header shell");
        }, TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task UnjoinedBlockHidesLiveEntriesEvenWhenFoldBoundaryStaysBehindSummary()
    {
        // A non-joined viewer must see the summary card only - never live entries. A non-joined
        // viewer never renders live entries, so the governed fold boundary never receives a viewport
        // signal and stays parked at its initial latch even as later summaries advance EndEntryLid -
        // the entries in that gap (between the parked fold end and the new summary end) must stay
        // hidden behind the card regardless.

        // arrange
        await Tester.SignInAsUniqueBob();
        var chat = await CreateSettledChat("unjoined-no-leak-test");
        var peerId = AuthorId.New(chat.Id, 777_350);
        var peer2Id = AuthorId.New(chat.Id, 777_351);
        var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
        await liveBackend.OnStreamRegistered(chat.Id, peerId, null, true, true, CancellationToken.None);
        await liveBackend.OnStreamRegistered(chat.Id, peer2Id, null, true, true, CancellationToken.None);
        var live = await liveBackend.GetState(chat.Id, CancellationToken.None);
        var v = live!.EffectiveVisibleStartLid;
        var spokenLids = new List<long>();
        for (var i = 0; i < 3; i++)
            spokenLids.Add((await CreateSpokenEntry(chat.Id, $"a-{i}")).LocalId);
        // Guards the arrangement, not the behavior: an entry that lands at V on its own - nothing
        // spoke it, so nothing hides it - reads to the assertions below exactly like a leak.
        spokenLids.Should().Equal([v, v + 1, v + 2], "the live entries must be the ones V points at");
        await liveBackend.UpdateSummary(chat.Id, new LiveSessionSummary {
            Title = "Recap", Description = "d", Summary = "s", EndEntryLid = v + 2, MessageCount = 3,
        }, CancellationToken.None);

        var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
        chatUI.SelectChatOnNavigation(chat.Id);   // Bob is NOT recording/listening => not joined
        var idRange = await Tester.Chats.GetIdRange(Tester.Session, chat.Id, CancellationToken.None);
        var query = new ChatDataQuery(idRange, -chatUI.HalfLoadLimit, chatUI.HalfLoadLimit);
        // latch the fold boundary at the first summary's end; the summarized live entries are hidden
        await ComputedTest.When(async ct => {
            var items = await chatUI.GetChatItems(chat.Id, query, 0, ct);
            items.Items.SelectMany(i => i.GetLeafMessages())
                .OfType<LiveConversationHeader>().Should().ContainSingle();
            LeafEntryLids(items).Should().NotContain([v, v + 1, v + 2],
                "a non-joined viewer sees the card only, not the summarized live entries");
        }, TimeSpan.FromSeconds(15));

        // act - two more entries land, then a second summary advances EndEntryLid past the parked boundary
        for (var i = 0; i < 2; i++)
            await CreateSpokenEntry(chat.Id, $"b-{i}");   // v+3, v+4
        await liveBackend.UpdateSummary(chat.Id, new LiveSessionSummary {
            Title = "Recap", Description = "d2", Summary = "s2", EndEntryLid = v + 4, MessageCount = 5,
        }, CancellationToken.None);

        // assert (sustained) - the newly-summarized entries beyond the parked boundary must never surface
        await Task.Delay(1000);
        var items2 = await chatUI.GetChatItems(chat.Id, query, 0, CancellationToken.None);
        LeafEntryLids(items2).Should().NotContain([v + 3, v + 4],
            "the fold boundary stays parked but the non-joined card must still hide the whole live range");
    }

    [Fact]
    public async Task CollapsedBlockFoldsUnsummarizedRowsAboveViewport()
    {
        // §4: the collapsed live block swallows everything above the viewport, even rows no summary
        // has ever covered - the boundary tracks the viewport top directly, with no summary gate.

        // arrange
        await Tester.SignInAsUniqueBob();
        var chat = await CreateSettledChat("swallow-above-viewport-test");
        var author = await Tester.GetOwnAuthor(chat.Id).Require();
        var peerId = AuthorId.New(chat.Id, 777_400);
        var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
        await liveBackend.OnStreamRegistered(chat.Id, author.Id, null, true, true, CancellationToken.None);
        await liveBackend.OnStreamRegistered(chat.Id, peerId, null, true, true, CancellationToken.None);
        var live = await liveBackend.GetState(chat.Id, CancellationToken.None);
        var v = live!.EffectiveVisibleStartLid;
        // A title so the block has a card, but the summary covers only V (EndEntryLid = v) - the entries
        // below are UN-summarised. Viewport tracking must still fold them once they scroll above the top.
        await liveBackend.UpdateSummary(chat.Id, new LiveSessionSummary {
            Title = "Recap", Description = "d", Summary = "s", EndEntryLid = v, MessageCount = 1,
        }, CancellationToken.None);
        var lids = new List<long>();
        // 5 rows above the viewport top, then a full MinTailEntryCount tail from it down - anything
        // shorter and the tail floor, not the summary gate, would be what holds the fold back.
        for (var i = 0; i < 5 + LiveFoldMath.MinTailEntryCount; i++)
            lids.Add((await Tester.CreateTextEntry(chat.Id, $"m-{i}")).LocalId);

        var chatAudioUI = Tester.ScopedAppServices.GetRequiredService<ChatAudioUI>();
        var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
        await chatAudioUI.SetRecordingChatId(chat.Id);   // Bob is a recorder => joined
        chatUI.SelectChatOnNavigation(chat.Id);
        await CollapseJoinedLiveBlock(chatUI, chat.Id, live.ToConversation());

        // act - viewport top sits at the 6th entry: everything above it (incl. un-summarised rows) must fold
        var viewportTop = lids[5];
        chatUI.SetItemVisibility(new ChatViewItemVisibility(
            chat.Id,
            new HashSet<ChatMessageKey> { ChatMessageKey.New(ChatMessageKind.None, viewportTop) },
            false,
            false));

        // assert - the governed boundary (not just the summary-covered range) advances to the viewport
        // top, so un-summarised rows above it are swallowed too
        var liveBlockUI = Tester.ScopedAppServices.GetRequiredService<LiveBlockUI>();
        await ComputedTest.When(async ct => {
            var blockState = await liveBlockUI.GetBlockState(chat.Id, ct);
            blockState.FoldBoundaryLid.Should().BeGreaterThanOrEqualTo(viewportTop,
                "the boundary tracks the viewport top, folding un-summarised rows above it");
        }, TimeSpan.FromSeconds(15));

        // assert - this must also hold at render level: the un-summarised rows above the viewport top
        // are folded into the block (not rendered as individual messages), while the viewport top and
        // everything below it still render normally
        var idRange = await Tester.Chats.GetIdRange(Tester.Session, chat.Id, CancellationToken.None);
        var query = new ChatDataQuery(idRange, -chatUI.HalfLoadLimit, chatUI.HalfLoadLimit);
        await ComputedTest.When(async ct => {
            var renderedLids = LeafEntryLids(await chatUI.GetChatItems(chat.Id, query, 0, ct));
            renderedLids.Should().NotContain(lids.Take(5),
                "un-summarised rows above the viewport top must fold, not render as individual messages");
            renderedLids.Should().Contain(lids.Skip(5),
                "the viewport top and the tail below it must still render normally");
        }, TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task StreamingTailSeparatesOwnSuppressionFromTheFold()
    {
        // One pass answers two questions: the floor covers whoever is speaking - that's what the live
        // block must not fold away - while suppression is only about the asking author's own
        // "Transcribing..." placeholder.

        // arrange
        await Tester.SignInAsUniqueBob();
        var chat = await CreateSettledChat("streaming-tail-test");
        var ownAuthor = await Tester.GetOwnAuthor(chat.Id).Require();
        var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
        await Tester.CreateTextEntry(chat.Id, "typed");

        // assert - a chat with no audio at all says nothing
        var quiet = await chatUI.GetStreamingTail(chat.Id, ownAuthor.Id, CancellationToken.None);
        quiet.FloorLid.Should().Be(long.MaxValue);
        quiet.IsSuppressed.Should().BeFalse();

        // act - my own transcript starts running
        var streaming = await Tester.CreateStreamingEntry(chat.Id, Languages.English);
        var streamingLid = streaming.ChatEntrySlim.LocalId;

        // assert
        await ComputedTest.When(async ct => {
            var s = await chatUI.GetStreamingTail(chat.Id, ownAuthor.Id, ct);
            s.FloorLid.Should().Be(streamingLid);
            s.IsSuppressed.Should().BeTrue("the placeholder stands down for my own running transcript");
            s.ExpiresAt.Should().BeNull("only a change lifts an indefinite suppression");
        }, TimeSpan.FromSeconds(10));

        // assert - asked on behalf of someone else: same floor, no suppression
        var forOther = await chatUI
            .GetStreamingTail(chat.Id, AuthorId.New(chat.Id, 777_430), CancellationToken.None);
        forOther.FloorLid.Should().Be(streamingLid, "the fold floor covers every speaker, not just me");
        forOther.IsSuppressed.Should().BeFalse("suppression is about the asking author's own placeholder");

        // act - the transcript closes
        await Tester.FinalizeStreamingEntry(streaming, "done");

        // assert - the grace window rides over the gap between utterances
        var graced = await chatUI.GetStreamingTail(chat.Id, ownAuthor.Id, CancellationToken.None);
        graced.FloorLid.Should().Be(streamingLid,
            "IsContentStreaming flickers false between utterances, so the answer must not");
        graced.IsSuppressed.Should().BeTrue();

        // assert - and lapses on its own
        await ComputedTest.When(async ct => {
            var s = await chatUI.GetStreamingTail(chat.Id, ownAuthor.Id, ct);
            s.FloorLid.Should().Be(long.MaxValue);
            s.IsSuppressed.Should().BeFalse();
        }, TimeSpan.FromSeconds(20));
    }

    [Fact]
    public async Task StreamingEntryStaysOutOfTheFold()
    {
        // A transcript that's still running must never be swallowed by the collapsed live block: the fold
        // boundary is held below it while it streams, and re-advances the moment it closes.

        // arrange
        await Tester.SignInAsUniqueBob();
        var chat = await CreateSettledChat("streaming-fold-test");
        var author = await Tester.GetOwnAuthor(chat.Id).Require();
        var peerId = AuthorId.New(chat.Id, 777_420);
        var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
        await liveBackend.OnStreamRegistered(chat.Id, author.Id, null, true, true, CancellationToken.None);
        await liveBackend.OnStreamRegistered(chat.Id, peerId, null, true, true, CancellationToken.None);
        var live = await liveBackend.GetState(chat.Id, CancellationToken.None);
        var v = live!.EffectiveVisibleStartLid;
        await liveBackend.UpdateSummary(chat.Id, new LiveSessionSummary {
            Title = "Recap", Description = "d", Summary = "s", EndEntryLid = v, MessageCount = 1,
        }, CancellationToken.None);
        for (var i = 0; i < 3; i++)
            await Tester.CreateTextEntry(chat.Id, $"before-{i}");
        var streaming = await Tester.CreateStreamingEntry(chat.Id, Languages.English);
        var streamingLid = streaming.ChatEntrySlim.LocalId;
        for (var i = 0; i < 10; i++)
            await Tester.CreateTextEntry(chat.Id, $"after-{i}");

        var chatAudioUI = Tester.ScopedAppServices.GetRequiredService<ChatAudioUI>();
        var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
        var liveBlockUI = Tester.ScopedAppServices.GetRequiredService<LiveBlockUI>();
        await chatAudioUI.SetRecordingChatId(chat.Id);
        chatUI.SelectChatOnNavigation(chat.Id);

        // act - the viewport top sits at the last entry, so the governor would otherwise fold everything above it
        var idRange = await Tester.Chats.GetIdRange(Tester.Session, chat.Id, CancellationToken.None);
        chatUI.SetItemVisibility(new ChatViewItemVisibility(
            chat.Id,
            new HashSet<ChatMessageKey> { ChatMessageKey.New(ChatMessageKind.None, idRange.End - 1) },
            false,
            false));

        // assert - the fold stops below the streaming entry
        await ComputedTest.When(async ct => {
            var streamingTail = await chatUI.GetStreamingTail(chat.Id, author.Id, ct);
            streamingTail.FloorLid.Should().Be(streamingLid);
            var s = await liveBlockUI.GetBlockState(chat.Id, ct);
            s.FoldBoundaryLid.Should().BeLessThanOrEqualTo(streamingLid,
                "a still-transcribing entry must stay outside the fold");
        }, TimeSpan.FromSeconds(15));

        // act - the transcript closes
        await Tester.FinalizeStreamingEntry(streaming, "done");

        // assert - the block re-compacts past it, without waiting on a fresh viewport signal: the
        // governor re-runs on the streaming tail's own invalidation and re-advances against the
        // viewport top it already holds
        await ComputedTest.When(async ct => {
            var streamingTail = await chatUI.GetStreamingTail(chat.Id, author.Id, ct);
            streamingTail.FloorLid.Should().Be(long.MaxValue);
            var s = await liveBlockUI.GetBlockState(chat.Id, ct);
            s.FoldBoundaryLid.Should().BeGreaterThan(streamingLid,
                "closing the transcript releases the fold");
        }, TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task LeaveThenStreamingFloorLapseMustNotRefoldTheBlock()
    {
        // §7: a transcript closing under the block must not re-open the fold. The floor lapsing to
        // "no cap" used to hand the fold straight back to the monotonic boundary the viewport had
        // reached earlier, swallowing the rows the reader was still looking at. LiveFoldMath.Advance
        // bounds every advance by the viewport top instead, so the lapse can't cross it. Leaving sits in
        // the middle of the same test because it has to change nothing - the block goes on as it was.

        // arrange
        await Tester.SignInAsUniqueBob();
        var chat = await CreateSettledChat("frozen-streaming-lapse-test");
        var author = await Tester.GetOwnAuthor(chat.Id).Require();
        var peerId = AuthorId.New(chat.Id, 777_430);
        var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
        await liveBackend.OnStreamRegistered(chat.Id, author.Id, null, true, true, CancellationToken.None);
        await liveBackend.OnStreamRegistered(chat.Id, peerId, null, true, true, CancellationToken.None);
        var live = await liveBackend.GetState(chat.Id, CancellationToken.None);
        var v = live!.EffectiveVisibleStartLid;
        await liveBackend.UpdateSummary(chat.Id, new LiveSessionSummary {
            Title = "Recap", Description = "d", Summary = "s", EndEntryLid = v, MessageCount = 1,
        }, CancellationToken.None);
        for (var i = 0; i < 3; i++)
            await Tester.CreateTextEntry(chat.Id, $"before-{i}");
        var streaming = await Tester.CreateStreamingEntry(chat.Id, Languages.English);
        var streamingLid = streaming.ChatEntrySlim.LocalId;
        for (var i = 0; i < LiveFoldMath.MinTailEntryCount; i++)
            await Tester.CreateTextEntry(chat.Id, $"after-{i}");

        var chatAudioUI = Tester.ScopedAppServices.GetRequiredService<ChatAudioUI>();
        var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
        var liveBlockUI = Tester.ScopedAppServices.GetRequiredService<LiveBlockUI>();
        await chatAudioUI.SetListeningState(chat.Id, true);
        chatUI.SelectChatOnNavigation(chat.Id);

        void SetViewportTop(long lid)
            => chatUI.SetItemVisibility(new ChatViewItemVisibility(
                chat.Id,
                new HashSet<ChatMessageKey> { ChatMessageKey.New(ChatMessageKind.None, lid) },
                false,
                false));

        // act - the reader reaches the live tail, so the fold would run well past the streaming entry,
        // and only the streaming floor holds it there
        var idRange = await Tester.Chats.GetIdRange(Tester.Session, chat.Id, CancellationToken.None);
        SetViewportTop(idRange.End - 1);
        await ComputedTest.When(async ct => {
            var s = await liveBlockUI.GetBlockState(chat.Id, ct);
            s.FoldBoundaryLid.Should().Be(streamingLid, "the streaming entry is what stops the fold");
        }, TimeSpan.FromSeconds(15));
        await CollapseJoinedLiveBlock(chatUI, chat.Id, live.ToConversation());

        // act - the reader scrolls back up onto the streaming entry, and that render is the baseline
        SetViewportTop(streamingLid);
        var query = new ChatDataQuery(idRange, -chatUI.HalfLoadLimit, chatUI.HalfLoadLimit);
        List<long> beforeLeaveLids = null!;
        await ComputedTest.When(async ct => {
            var s = await liveBlockUI.GetBlockState(chat.Id, ct);
            s.FoldBoundaryLid.Should().Be(streamingLid, "the streaming entry still stops the fold");
            beforeLeaveLids = LeafEntryLids(await chatUI.GetChatItems(chat.Id, query, 0, ct));
        }, TimeSpan.FromSeconds(15));

        // act - the reader leaves
        await chatAudioUI.SetListeningState(chat.Id, false);
        InvalidateAmIInLiveConversation(chatAudioUI, chat.Id);
        await ComputedTest.When(async ct => {
            var s = await liveBlockUI.GetBlockState(chat.Id, ct);
            s.Overlay.Should().NotBeNull("the overlay is what marks this viewer as having been there");
        }, TimeSpan.FromSeconds(10));

        // assert - leaving is only "stop listening": same fold, same rows
        LeafEntryLids(await chatUI.GetChatItems(chat.Id, query, 0, CancellationToken.None))
            .Should().Equal(beforeLeaveLids, "leaving must not change what the block renders");

        // act - the transcript closes under the block, so its floor lapses to "no cap"
        await Tester.FinalizeStreamingEntry(streaming, "done");
        await ComputedTest.When(async ct => {
            var streamingTail = await chatUI.GetStreamingTail(chat.Id, author.Id, ct);
            streamingTail.FloorLid.Should().Be(long.MaxValue, "the floor has to actually lapse");
        }, TimeSpan.FromSeconds(15));

        // assert - the lapse may only let the fold reach the viewport top, which is the streaming row
        // itself, so nothing the reader had is swallowed
        await Task.Delay(500);
        var afterLapse = await liveBlockUI.GetBlockState(chat.Id, CancellationToken.None);
        afterLapse.FoldBoundaryLid.Should().BeLessThanOrEqualTo(streamingLid,
            "a lapsing floor must not push the fold past the reader's viewport top");
        LeafEntryLids(await chatUI.GetChatItems(chat.Id, query, 0, CancellationToken.None))
            .Should().Equal(beforeLeaveLids, "closing the transcript must not re-fold the block");
    }

    [Fact]
    public async Task RevealMoreRetreatsEffectiveFoldAndPersists()
    {
        // §7: RevealMore retreats RevealedBoundaryLid below the governor's monotonic FoldBoundaryLid,
        // so the effective fold (min of the two) survives further governor advances; ResetReveal clears it.

        // arrange
        await Tester.SignInAsUniqueBob();
        var chat = await CreateSettledChat("reveal-more-test");
        var author = await Tester.GetOwnAuthor(chat.Id).Require();
        var peerId = AuthorId.New(chat.Id, 777_410);
        var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
        await liveBackend.OnStreamRegistered(chat.Id, author.Id, null, true, true, CancellationToken.None);
        await liveBackend.OnStreamRegistered(chat.Id, peerId, null, true, true, CancellationToken.None);
        var live = await liveBackend.GetState(chat.Id, CancellationToken.None);
        var v = live!.EffectiveVisibleStartLid;
        await liveBackend.UpdateSummary(chat.Id, new LiveSessionSummary {
            Title = "Recap", Description = "d", Summary = "s", EndEntryLid = v, MessageCount = 1,
        }, CancellationToken.None);
        for (var i = 0; i < 20; i++)
            await Tester.CreateTextEntry(chat.Id, $"m-{i}");

        var chatAudioUI = Tester.ScopedAppServices.GetRequiredService<ChatAudioUI>();
        var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
        var liveBlockUI = Tester.ScopedAppServices.GetRequiredService<LiveBlockUI>();
        await chatAudioUI.SetRecordingChatId(chat.Id);
        chatUI.SelectChatOnNavigation(chat.Id);

        // act - viewport top sits at the last entry, so a large range folds
        var idRange = await Tester.Chats.GetIdRange(Tester.Session, chat.Id, CancellationToken.None);
        var viewportTop = idRange.End - 1;
        chatUI.SetItemVisibility(new ChatViewItemVisibility(
            chat.Id,
            new HashSet<ChatMessageKey> { ChatMessageKey.New(ChatMessageKind.None, viewportTop) },
            false,
            false));

        long foldedBoundary = 0;
        await ComputedTest.When(async ct => {
            var s = await liveBlockUI.GetBlockState(chat.Id, ct);
            s.FoldBoundaryLid.Should().BeGreaterThan(v + 5);
            foldedBoundary = s.FoldBoundaryLid;
        }, TimeSpan.FromSeconds(15));

        // act - reveal one batch
        await liveBlockUI.RevealMore(chat.Id);

        // assert - the effective fold boundary retreats below where the governor had it
        await ComputedTest.When(async ct => {
            var s = await liveBlockUI.GetBlockState(chat.Id, ct);
            Math.Min(s.FoldBoundaryLid, s.RevealedBoundaryLid).Should().BeLessThan(foldedBoundary,
                "revealing a batch retreats the effective fold boundary");
        }, TimeSpan.FromSeconds(10));

        // assert - it survives a further governor advance (viewport unchanged, so nothing pushes it back up)
        await Task.Delay(500);
        var afterReveal = await liveBlockUI.GetBlockState(chat.Id, CancellationToken.None);
        Math.Min(afterReveal.FoldBoundaryLid, afterReveal.RevealedBoundaryLid).Should().BeLessThan(foldedBoundary,
            "the revealed boundary persists across governor re-evaluation");

        // act - reset clears the reveal
        liveBlockUI.ResetReveal(chat.Id);
        await ComputedTest.When(async ct => {
            var s = await liveBlockUI.GetBlockState(chat.Id, ct);
            s.RevealedBoundaryLid.Should().Be(long.MaxValue, "reset clears the reveal");
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task RevealReSwallowsOnReturnToTail()
    {
        // §7: a reveal is a temporary peek - once the reader scrolls back down so every revealed row is
        // above the viewport again, the governor re-swallows them (clears the reveal). A "scrolled-into"
        // latch keeps it from re-collapsing before the reader has actually entered the revealed region.

        // arrange
        await Tester.SignInAsUniqueBob();
        var chat = await CreateSettledChat("reveal-reswallow-test");
        var author = await Tester.GetOwnAuthor(chat.Id).Require();
        var peerId = AuthorId.New(chat.Id, 777_411);
        var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
        await liveBackend.OnStreamRegistered(chat.Id, author.Id, null, true, true, CancellationToken.None);
        await liveBackend.OnStreamRegistered(chat.Id, peerId, null, true, true, CancellationToken.None);
        var live = await liveBackend.GetState(chat.Id, CancellationToken.None);
        var v = live!.EffectiveVisibleStartLid;
        await liveBackend.UpdateSummary(chat.Id, new LiveSessionSummary {
            Title = "Recap", Description = "d", Summary = "s", EndEntryLid = v, MessageCount = 1,
        }, CancellationToken.None);
        for (var i = 0; i < 20; i++)
            await Tester.CreateTextEntry(chat.Id, $"m-{i}");

        var chatAudioUI = Tester.ScopedAppServices.GetRequiredService<ChatAudioUI>();
        var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
        var liveBlockUI = Tester.ScopedAppServices.GetRequiredService<LiveBlockUI>();
        await chatAudioUI.SetRecordingChatId(chat.Id);
        chatUI.SelectChatOnNavigation(chat.Id);

        void SetViewportTop(long lid)
            => chatUI.SetItemVisibility(new ChatViewItemVisibility(
                chat.Id,
                new HashSet<ChatMessageKey> { ChatMessageKey.New(ChatMessageKind.None, lid) },
                false,
                false));

        // drive the fold boundary up to the live tail
        var idRange = await Tester.Chats.GetIdRange(Tester.Session, chat.Id, CancellationToken.None);
        var tailTop = idRange.End - 1;
        SetViewportTop(tailTop);
        long boundary = 0;
        await ComputedTest.When(async ct => {
            var s = await liveBlockUI.GetBlockState(chat.Id, ct);
            s.FoldBoundaryLid.Should().BeGreaterThan(v + 5);
            boundary = s.FoldBoundaryLid;
        }, TimeSpan.FromSeconds(15));

        // reveal a batch
        await liveBlockUI.RevealMore(chat.Id);
        long revealed = 0;
        await ComputedTest.When(async ct => {
            var s = await liveBlockUI.GetBlockState(chat.Id, ct);
            s.RevealedBoundaryLid.Should().BeLessThan(boundary, "reveal retreats below the boundary");
            revealed = s.RevealedBoundaryLid;
        }, TimeSpan.FromSeconds(10));

        // scroll UP into the revealed region: the reveal must persist (only the latch flips here)
        SetViewportTop(revealed);
        await Task.Delay(500);
        var whileReading = await liveBlockUI.GetBlockState(chat.Id, CancellationToken.None);
        whileReading.RevealedBoundaryLid.Should().Be(revealed,
            "the reveal persists while the reader is inside the revealed region");

        // scroll back DOWN to the live tail: every revealed row is now above the viewport -> re-swallow
        SetViewportTop(tailTop);
        await ComputedTest.When(async ct => {
            var s = await liveBlockUI.GetBlockState(chat.Id, ct);
            s.RevealedBoundaryLid.Should().Be(long.MaxValue,
                "returning to the live tail re-swallows the revealed batch");
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task RevealMoreSurvivesLeaveFreeze()
    {
        // §7 cross-task fix: a revealed batch must survive the freeze on leave/close - DeriveOverlay
        // must use the reveal-aware effective boundary, not the raw monotonic FoldBoundaryLid, or
        // leaving re-folds the rows the reader just revealed (a content shrink right under them).

        // arrange
        await Tester.SignInAsUniqueBob();
        var chat = await CreateSettledChat("reveal-leave-freeze-test");
        var author = await Tester.GetOwnAuthor(chat.Id).Require();
        var peerId = AuthorId.New(chat.Id, 777_420);
        var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
        await liveBackend.OnStreamRegistered(chat.Id, author.Id, null, true, true, CancellationToken.None);
        await liveBackend.OnStreamRegistered(chat.Id, peerId, null, true, true, CancellationToken.None);
        var live = await liveBackend.GetState(chat.Id, CancellationToken.None);
        var v = live!.EffectiveVisibleStartLid;
        await liveBackend.UpdateSummary(chat.Id, new LiveSessionSummary {
            Title = "Recap", Description = "d", Summary = "s", EndEntryLid = v, MessageCount = 1,
        }, CancellationToken.None);
        // Enough that one reveal batch leaves rows still folded: revealing the whole backlog would
        // collapse FoldRange to the empty range, and the freeze would have nothing to preserve.
        for (var i = 0; i < 40; i++)
            await Tester.CreateTextEntry(chat.Id, $"m-{i}");

        var chatAudioUI = Tester.ScopedAppServices.GetRequiredService<ChatAudioUI>();
        var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
        var liveBlockUI = Tester.ScopedAppServices.GetRequiredService<LiveBlockUI>();
        await chatAudioUI.SetListeningState(chat.Id, true);
        chatUI.SelectChatOnNavigation(chat.Id);

        // act - viewport top sits at the last entry, so a large range folds
        var idRange = await Tester.Chats.GetIdRange(Tester.Session, chat.Id, CancellationToken.None);
        var viewportTop = idRange.End - 1;
        chatUI.SetItemVisibility(new ChatViewItemVisibility(
            chat.Id,
            new HashSet<ChatMessageKey> { ChatMessageKey.New(ChatMessageKind.None, viewportTop) },
            false,
            false));

        long foldedBoundary = 0;
        await ComputedTest.When(async ct => {
            var s = await liveBlockUI.GetBlockState(chat.Id, ct);
            s.FoldBoundaryLid.Should().BeGreaterThan(v + 5);
            foldedBoundary = s.FoldBoundaryLid;
        }, TimeSpan.FromSeconds(15));

        // act - reveal one batch, retreating the effective boundary below the raw governed one
        await liveBlockUI.RevealMore(chat.Id);
        long revealedEffectiveBoundary = 0;
        await ComputedTest.When(async ct => {
            var s = await liveBlockUI.GetBlockState(chat.Id, ct);
            revealedEffectiveBoundary = Math.Min(s.FoldBoundaryLid, s.RevealedBoundaryLid);
            revealedEffectiveBoundary.Should().BeLessThan(foldedBoundary);
        }, TimeSpan.FromSeconds(10));

        // act - leave: hang up, which freezes the block via the overlay
        await chatAudioUI.SetListeningState(chat.Id, false);
        InvalidateAmIInLiveConversation(chatAudioUI, chat.Id);

        // assert - the frozen overlay's FoldRange must end at the reveal-aware effective boundary,
        // not the full monotonic FoldBoundaryLid the governor had reached before the reveal
        await ComputedTest.When(async ct => {
            var s = await liveBlockUI.GetBlockState(chat.Id, ct);
            s.Overlay.Should().NotBeNull();
            s.Overlay!.FoldRange.End.Should().Be(revealedEffectiveBoundary,
                "the freeze must preserve what the reader had revealed, not re-fold it back under them");
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task CollapsedJoinedBlockReportsSwallowedCount()
    {
        // §7: SwallowedCount is the true message count folded in [V, effectiveBoundary) - not a lid
        // span (lids have gaps) - and it must drop to 0 once RevealMore walks the whole backlog back
        // into view.

        // arrange
        await Tester.SignInAsUniqueBob();
        var chat = await CreateSettledChat("swallowed-count-test");
        var author = await Tester.GetOwnAuthor(chat.Id).Require();
        var peerId = AuthorId.New(chat.Id, 777_420);
        var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
        await liveBackend.OnStreamRegistered(chat.Id, author.Id, null, true, true, CancellationToken.None);
        await liveBackend.OnStreamRegistered(chat.Id, peerId, null, true, true, CancellationToken.None);
        var live = await liveBackend.GetState(chat.Id, CancellationToken.None);
        var v = live!.EffectiveVisibleStartLid;
        await liveBackend.UpdateSummary(chat.Id, new LiveSessionSummary {
            Title = "Recap", Description = "d", Summary = "s", EndEntryLid = v, MessageCount = 1,
        }, CancellationToken.None);
        var lids = new List<long>();
        for (var i = 0; i < 3; i++)
            lids.Add((await Tester.CreateTextEntry(chat.Id, $"m-{i}")).LocalId);
        // A system entry sits inside the swallowed range - GetSwallowedCount must skip it (it filters
        // on IsSystemEntry, same as RevealMore), so the true message count (5) differs from the lid
        // span across the folded range (6). This guards the "never approximate with a lid span"
        // invariant: a wrong `effectiveBoundary - v` implementation would report 6, not 5, here.
        await Tester.Commander.Call(new ChatsBackend_ChangeEntry(
            ChatEntryId.New(chat.Id, 0),
            null,
            Change.Create(new ChatEntryDiff {
                Kind = ChatEntryKind.MembersChanged,
                AuthorId = author.Id,
                TargetAuthorId = peerId,
                TargetAuthorName = "Peer",
                HasLeft = false,
            })));
        // 5 rows above the viewport top, then a full MinTailEntryCount tail from it down, or the tail
        // floor would hold the fold back and there'd be nothing swallowed to count.
        for (var i = 3; i < 5 + LiveFoldMath.MinTailEntryCount; i++)
            lids.Add((await Tester.CreateTextEntry(chat.Id, $"m-{i}")).LocalId);

        var chatAudioUI = Tester.ScopedAppServices.GetRequiredService<ChatAudioUI>();
        var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
        var liveBlockUI = Tester.ScopedAppServices.GetRequiredService<LiveBlockUI>();
        await chatAudioUI.SetRecordingChatId(chat.Id);
        chatUI.SelectChatOnNavigation(chat.Id);

        // act - viewport top sits at the 6th real entry, folding the 5 real rows above it (plus the
        // interleaved system entry, which must not count towards SwallowedCount)
        var viewportTop = lids[5];
        chatUI.SetItemVisibility(new ChatViewItemVisibility(
            chat.Id,
            new HashSet<ChatMessageKey> { ChatMessageKey.New(ChatMessageKind.None, viewportTop) },
            false,
            false));

        // assert - the count is the true number of folded messages, not a lid-span approximation
        await ComputedTest.When(async ct => {
            var count = await liveBlockUI.GetSwallowedCount(chat.Id, ct);
            count.Should().Be(5, "exactly the 5 rows above the viewport top are folded");
        }, TimeSpan.FromSeconds(15));

        // act - reveal the whole backlog in one batch (5 folded rows <= the rounded-up batch size)
        await liveBlockUI.RevealMore(chat.Id);

        // assert - the count drops to 0 once every folded row has been revealed
        await ComputedTest.When(async ct => {
            var count = await liveBlockUI.GetSwallowedCount(chat.Id, ct);
            count.Should().Be(0, "revealing the whole backlog leaves nothing swallowed");
        }, TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task ShouldRememberTheLastKnownBlockSnapshot()
    {
        // The stand-in a non-waiting caller falls back to: if it stays empty, the live block would
        // collapse to "no session" on every rebuild that outruns the remote read.

        // arrange
        await Tester.SignInAsUniqueBob();
        var chat = await CreateSettledChat("live-snapshot-memory-test");
        var author = await Tester.GetOwnAuthor(chat.Id).Require();
        var peerId = AuthorId.New(chat.Id, 777_090);
        var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
        await liveBackend.OnStreamRegistered(chat.Id, author.Id, null, true, true, CancellationToken.None);
        await liveBackend.OnStreamRegistered(chat.Id, peerId, null, true, true, CancellationToken.None);
        var liveSessionUI = Tester.ScopedAppServices.GetRequiredService<LiveSessionUI>();

        // act
        var snapshot = await liveSessionUI.GetBlockSnapshot(chat.Id, CancellationToken.None);

        // assert
        snapshot.Should().NotBeNull();
        snapshot!.IsLatched.Should().BeTrue("two registered streams latch the session");
        liveSessionUI.GetLastKnownBlockSnapshot(chat.Id)
            .Should().Be(snapshot, "the computed read must leave a stand-in behind");
    }

    [Fact]
    public async Task RestartedSessionSurfacesItsEntriesForAViewerWhoLeftTheLastOne()
    {
        // Everyone hangs up, and then someone starts talking again while the viewer is not attending.
        // The viewer was in the session that ended, so the block they are still looking at is a frozen
        // one - and its hidden tail must not be allowed to swallow what the next session says.

        // arrange
        await Tester.SignInAsUniqueBob();
        var chat = await CreateSettledChat("restart-after-close-test");
        var author = await Tester.GetOwnAuthor(chat.Id).Require();
        var peerId = AuthorId.New(chat.Id, 777_140);
        var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
        await liveBackend.OnStreamRegistered(chat.Id, author.Id, null, true, true, CancellationToken.None);
        await liveBackend.OnStreamRegistered(chat.Id, peerId, null, true, true, CancellationToken.None);
        var live = await liveBackend.GetState(chat.Id, CancellationToken.None);
        live.Should().NotBeNull();
        var v = live!.EffectiveVisibleStartLid;
        for (var i = 0; i < 3; i++)
            await CreateSpokenEntry(chat.Id, $"first-{i}");
        await liveBackend.UpdateSummary(chat.Id,
            new LiveSessionSummary {
                Title = "Recap", Description = "d", Summary = "s",
                EndEntryLid = v + 2, MessageCount = 3,
            }, CancellationToken.None);

        var chatAudioUI = Tester.ScopedAppServices.GetRequiredService<ChatAudioUI>();
        var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
        await chatAudioUI.SetListeningState(chat.Id, true);
        chatUI.SelectChatOnNavigation(chat.Id);
        var idRange = await Tester.Chats.GetIdRange(Tester.Session, chat.Id, CancellationToken.None);
        var query = new ChatDataQuery(idRange, -chatUI.HalfLoadLimit, chatUI.HalfLoadLimit);
        await ComputedTest.When(async ct => {
            var items = await chatUI.GetChatItems(chat.Id, query, 0, ct);
            items.Items.OfType<ExpandedConversationMessage>().Should().ContainSingle();
        }, TimeSpan.FromSeconds(10));

        // Everyone stops, the viewer included - which is what latches the frozen template. No
        // FinalizeSession: the report restarts "almost instantly", i.e. while the session is still
        // closing rather than after it has been put away.
        await chatAudioUI.SetListeningState(chat.Id, false);
        InvalidateAmIInLiveConversation(chatAudioUI, chat.Id);
        await liveBackend.SetParticipation(chat.Id, peerId, ParticipationKind.Record, false, CancellationToken.None);
        await liveBackend.SetParticipation(chat.Id, author.Id, ParticipationKind.Record, false, CancellationToken.None);
        var liveBlockUI = Tester.ScopedAppServices.GetRequiredService<LiveBlockUI>();
        await ComputedTest.When(async ct => {
            var blockState = await liveBlockUI.GetBlockState(chat.Id, ct);
            blockState.Overlay.Should().NotBeNull();
        }, TimeSpan.FromSeconds(10));
        var afterStop = await liveBlockUI.GetBlockState(chat.Id, CancellationToken.None);
        var stateAfterStop = await liveBackend.GetState(chat.Id, CancellationToken.None);
        Out.WriteLine($"after stop: overlay={Describe(afterStop)}");
        Out.WriteLine($"after stop: session={(stateAfterStop == null ? "<null>" : $"v={stateAfterStop.EffectiveVisibleStartLid}, isClosing={stateAfterStop.IsClosing}, authors={stateAfterStop.AuthorIds.Count}, end={stateAfterStop.EndEntryLid}")}");

        // act - one of them starts talking again, and says something
        await liveBackend.OnStreamRegistered(chat.Id, peerId, null, true, true, CancellationToken.None);
        var restarted = await liveBackend.GetState(chat.Id, CancellationToken.None);
        Out.WriteLine($"after restart: session={(restarted == null ? "<null>" : $"v={restarted.EffectiveVisibleStartLid}, isClosing={restarted.IsClosing}, authors={restarted.AuthorIds.Count}, end={restarted.EndEntryLid}")}");
        var spoken = new List<ChatEntry>();
        for (var i = 0; i < 3; i++)
            spoken.Add(await CreateSpokenEntry(chat.Id, $"second-{i}"));
        var typed = new List<ChatEntry>();
        for (var i = 0; i < 12; i++)
            typed.Add(await Tester.CreateTextEntry(chat.Id, $"typed after restart {i}"));
        await Task.Delay(2000);

        // assert - the block must not still be frozen against the session that ended. A viewer who is
        // not attending never sees a live block's entries, by design; what they do see is the card's
        // tail preview, and ConversationMessageView gates that on there being no overlay. Left frozen,
        // the card shows nothing of what is being said, and its hidden tail runs to long.MaxValue - so
        // the restart's transcript only surfaces once the conversation ends and materializes.
        var afterRestart = await liveBlockUI.GetBlockState(chat.Id, CancellationToken.None);
        Out.WriteLine($"after restart: overlay={Describe(afterRestart)}");
        afterRestart.Overlay.Should().BeNull();
        var finalItems = await chatUI.GetChatItems(chat.Id, query, 0, CancellationToken.None);
        var finalLids = LeafEntryLids(finalItems);
        Out.WriteLine($"spoken lids: {string.Join(", ", spoken.Select(e => e.Id.LocalId))}");
        Out.WriteLine($"visible lids: {string.Join(", ", finalLids)}");
        finalLids.Should().Contain(typed.Select(e => e.Id.LocalId));
    }

    [Fact]
    public async Task ConversationOverlappingTheLiveBlockRendersAsPlainEntries()
    {
        // A regular conversation whose range runs into the live block's can't render as a block - both
        // claim the same rows - so it degrades to plain entries. This pins the SERVER's half of that:
        // GetRangeMeta rebuilds the ranges so an overlapping record's id never reaches the client, and
        // ConversationRangeMeta.ConversationIds is derived from those rebuilt ranges. The client-side
        // filter in ChatUI.Tiles.GetTile guards the straddling case instead (StartEntryLid < V <=
        // EndEntryLid, reported by tiles ending at or before V), which needs a chat spanning more than
        // one server id tile to reproduce - too big to build here until the tile flattening lands.

        // arrange
        await Tester.SignInAsUniqueBob();
        var chat = await CreateSettledChat("conversation-overlap-test");
        var author = await Tester.GetOwnAuthor(chat.Id).Require();
        var peerId = AuthorId.New(chat.Id, 777_440);
        var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();

        var before = new List<ChatEntry>();
        for (var i = 0; i < 3; i++)
            before.Add(await Tester.CreateTextEntry(chat.Id, $"before-{i}"));
        // Ends exactly where the live block starts, so it must keep its card: EntryLidRange is
        // half-open, so a touching pair intersects to nothing.
        var neighbor = new Conversation(ConversationId.New(chat.Id, before[0].LocalId), 1) {
            Title = "Earlier", Description = "d", Summary = "s", MessageCount = before.Count,
            EndEntryLid = before[^1].LocalId,
            StartsAt = before[0].BeginsAt, EndsAt = before[^1].BeginsAt,
        };
        await Tester.Commander.Call(new ConversationBackend_Materialize(neighbor));

        var liveStart = await Tester.CreateTextEntry(chat.Id, "live-start");
        await liveBackend.OnStreamRegistered(
            chat.Id, author.Id, liveStart.LocalId, true, true, CancellationToken.None);
        await liveBackend.OnStreamRegistered(chat.Id, peerId, null, true, true, CancellationToken.None);
        var live = await liveBackend.GetState(chat.Id, CancellationToken.None);
        var v = live!.EffectiveVisibleStartLid;
        var inside = new List<ChatEntry>();
        // The overlap has to cover whole aligned id tiles: a tile is only dropped when an excluded
        // range contains all of it, so a conversation narrower than a tile never exercises the load
        // path at all.
        var tileSize = ChatUI.IdTileStack.FirstLayer.TileSize;
        for (var i = 0; i < 4 * tileSize; i++)
            inside.Add(await Tester.CreateTextEntry(chat.Id, $"inside-{i}"));
        await liveBackend.UpdateSummary(chat.Id, new LiveSessionSummary {
            Title = "Recap", Description = "d", Summary = "s", EndEntryLid = v, MessageCount = 1,
        }, CancellationToken.None);
        // The overlap: a persisted conversation sitting inside the live block's range - which is what id
        // churn leaves behind when V latches inside an older record.
        var overlapStart = inside.First(e => e.LocalId % tileSize == 0).LocalId;
        var overlapEnd = overlapStart + (2 * tileSize) - 1;
        var overlapping = new Conversation(ConversationId.New(chat.Id, overlapStart), 1) {
            Title = "Overlapping", Description = "d", Summary = "s", MessageCount = (int)(2 * tileSize),
            EndEntryLid = overlapEnd,
            StartsAt = inside[0].BeginsAt, EndsAt = inside[^1].BeginsAt,
        };
        await Tester.Commander.Call(new ConversationBackend_Materialize(overlapping));

        var chatAudioUI = Tester.ScopedAppServices.GetRequiredService<ChatAudioUI>();
        var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
        await chatAudioUI.SetListeningState(chat.Id, true);   // joined => the block expands and shows rows
        chatUI.SelectChatOnNavigation(chat.Id);

        // act
        var idRange = await Tester.Chats.GetIdRange(Tester.Session, chat.Id, CancellationToken.None);
        var query = new ChatDataQuery(idRange, -chatUI.HalfLoadLimit, chatUI.HalfLoadLimit);

        // assert
        await ComputedTest.When(async ct => {
            var items = await chatUI.GetChatItems(chat.Id, query, 0, ct);
            var cardIds = items.Items
                .SelectMany(i => i.GetLeafMessages())
                .OfType<ConversationMessage>()
                .Select(m => m.Conversation!.Id)
                .ToList();
            cardIds.Should().NotContain(overlapping.Id,
                "an overlapping conversation must not render as a block");
            cardIds.Should().Contain(neighbor.Id,
                "a conversation ending exactly where the live block starts doesn't overlap it");
            LeafEntryLids(items).Should().Contain(
                inside.Where(e => e.LocalId >= overlapStart && e.LocalId <= overlapEnd).Select(e => e.LocalId),
                "the overlapping conversation's entries, and the live block's own rows, still render");
        }, TimeSpan.FromSeconds(20));
    }

    [Fact]
    public async Task CollapsedBlockSurfacesThreadsWhetherSpokenOrTyped()
    {
        // A collapsed block hides what the call spoke, but a thread start heads a discussion rather
        // than being just a transcript row, so it stays reachable below the card either way. Hiding on
        // HasAudio alone dropped the transcript-born ones in GetTile, before they could become thread
        // cards at all, so only threads started from typed messages ever surfaced.

        // arrange
        await Tester.SignInAsUniqueBob();
        var chat = await CreateSettledChat("collapsed-thread-surfacing-test");
        var author = await Tester.GetOwnAuthor(chat.Id).Require();
        var peerId = AuthorId.New(chat.Id, 777_450);
        var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
        await liveBackend.OnStreamRegistered(chat.Id, author.Id, null, true, true, CancellationToken.None);
        await liveBackend.OnStreamRegistered(chat.Id, peerId, null, true, true, CancellationToken.None);
        var live = await liveBackend.GetState(chat.Id, CancellationToken.None);
        var v = live!.EffectiveVisibleStartLid;
        await liveBackend.UpdateSummary(chat.Id, new LiveSessionSummary {
            Title = "Recap", Description = "d", Summary = "s", EndEntryLid = v, MessageCount = 1,
        }, CancellationToken.None);
        var spokenPlain = await CreateSpokenEntry(chat.Id, "spoken plain");
        var spokenThreadStart = await CreateSpokenEntry(chat.Id, "spoken thread start");
        var typedThreadStart = await Tester.CreateTextEntry(chat.Id, "typed thread start");
        await StartThread(chat.Id, spokenThreadStart.Id, "Spoken thread");
        await StartThread(chat.Id, typedThreadStart.Id, "Typed thread");

        var chatAudioUI = Tester.ScopedAppServices.GetRequiredService<ChatAudioUI>();
        var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
        await chatAudioUI.SetListeningState(chat.Id, true);
        chatUI.SelectChatOnNavigation(chat.Id);

        // act
        await CollapseJoinedLiveBlock(chatUI, chat.Id, live.ToConversation());

        // assert
        var idRange = await Tester.Chats.GetIdRange(Tester.Session, chat.Id, CancellationToken.None);
        var query = new ChatDataQuery(idRange, -chatUI.HalfLoadLimit, chatUI.HalfLoadLimit);
        await ComputedTest.When(async ct => {
            var items = await chatUI.GetChatItems(chat.Id, query, 0, ct);
            var threadLids = items.Items
                .SelectMany(i => i.GetLeafMessages())
                .OfType<ThreadMessage>()
                .Select(m => m.Id)
                .ToList();
            threadLids.Should().Contain(spokenThreadStart.Id.LocalId,
                "a thread born from a transcript is still a thread");
            threadLids.Should().Contain(typedThreadStart.Id.LocalId,
                "a thread born from a typed message surfaces the same way");
            LeafEntryLids(items).Should().NotContain(spokenPlain.Id.LocalId,
                "a plain spoken row is still what the collapsed card stands for");
            // Below the card, not inside it: the collapsed block is one unit, so a thread it happens to
            // span is placed by the ordinary rules.
            items.Items.OfType<ThreadMessage>().Select(m => m.Id).Should().Contain(
                [spokenThreadStart.Id.LocalId, typedThreadStart.Id.LocalId],
                "a collapsed block must not absorb the threads inside its range");
        }, TimeSpan.FromSeconds(20));
    }

    [Fact]
    public async Task ExpandedBlockKeepsItsThreadsInside()
    {
        // The counterpart: a block showing its rows owns the threads inside its range, as it always has.

        // arrange
        await Tester.SignInAsUniqueBob();
        var chat = await CreateSettledChat("expanded-thread-absorb-test");
        var author = await Tester.GetOwnAuthor(chat.Id).Require();
        var peerId = AuthorId.New(chat.Id, 777_460);
        var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
        await liveBackend.OnStreamRegistered(chat.Id, author.Id, null, true, true, CancellationToken.None);
        await liveBackend.OnStreamRegistered(chat.Id, peerId, null, true, true, CancellationToken.None);
        var live = await liveBackend.GetState(chat.Id, CancellationToken.None);
        var v = live!.EffectiveVisibleStartLid;
        var threadStart = await Tester.CreateTextEntry(chat.Id, "typed thread start");
        await liveBackend.UpdateSummary(chat.Id, new LiveSessionSummary {
            Title = "Recap", Description = "d", Summary = "s",
            EndEntryLid = threadStart.LocalId, MessageCount = 2,
        }, CancellationToken.None);
        await StartThread(chat.Id, threadStart.Id, "Typed thread");

        var chatAudioUI = Tester.ScopedAppServices.GetRequiredService<ChatAudioUI>();
        var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
        await chatAudioUI.SetListeningState(chat.Id, true);   // joined => the block expands
        chatUI.SelectChatOnNavigation(chat.Id);

        // act + assert
        var idRange = await Tester.Chats.GetIdRange(Tester.Session, chat.Id, CancellationToken.None);
        var query = new ChatDataQuery(idRange, -chatUI.HalfLoadLimit, chatUI.HalfLoadLimit);
        await ComputedTest.When(async ct => {
            var items = await chatUI.GetChatItems(chat.Id, query, 0, ct);
            chatUI.IsConversationExpanded(live.ToConversation()).Should().BeTrue();
            items.Items.OfType<ThreadMessage>().Should().BeEmpty(
                "an expanded block absorbs the threads inside its range");
            items.Items
                .SelectMany(i => i.GetLeafMessages())
                .OfType<ThreadMessage>()
                .Select(m => m.Id)
                .Should().Contain(threadStart.LocalId, "the thread is inside the block, not gone");
        }, TimeSpan.FromSeconds(20));
    }

    // Private methods

    private Task StartThread(ChatId chatId, ChatEntryId entryId, string title)
        => Tester.Commander.Call(new ChatThreads_Start {
            Session = Tester.Session,
            ParentChatId = chatId,
            Title = title,
            Description = "",
            EntryIds = [entryId],
        });

    private static string Describe(LiveBlockState? state)
    {
        var overlay = state?.Overlay;
        return overlay == null
            ? "<null>"
            : $"renderId={overlay.RenderId}, cardLid={overlay.CardLid}, hiddenTail={overlay.HiddenTailRange}, "
                + $"foldRange={overlay.FoldRange}, blockEnd={overlay.BlockEndLid}, "
                + $"materialized={overlay.MaterializedId}, wasAttending={state!.WasAttending}";
    }

    private static void InvalidateAmIInLiveConversation(ChatAudioUI chatAudioUI, ChatId chatId)
    {
        // ChatAudioUI.GetState reads ActiveChatsUI.ActiveChats.Value directly rather than reactively,
        // so it only stays in sync via its own background worker (InvalidateActiveChatDependencies)
        // explicitly invalidating it after a listening-state change - mirror that invalidation here so
        // AmIInLiveConversation (and therefore the governor) reflects SetListeningState immediately,
        // without depending on that worker's timing within this test's lifetime.
        using (Invalidation.Begin())
            _ = chatAudioUI.GetState(chatId);
    }

    private static void AssertUniqueRenderKeys(ChatItems items)
    {
        AssertUniqueKeys(items.Items.Select(i => ((IVirtualListItem)i).RenderKey), "list", items);
        foreach (var block in items.Items.OfType<ExpandedConversationMessage>())
            AssertUniqueKeys(block.Items.Select(i => i.Key.Value), $"block {block.Key}", items);

        // Stronger than @key uniqueness: a conversation owns exactly one start and one end band
        // anywhere in the tree, even when the two land in different sibling lists.
        var leaves = items.Items.SelectMany(i => i.GetLeafMessages()).ToList();
        AssertUniqueKeys(
            leaves.Where(m => m.Kind == ChatMessageKind.ConversationEnd)
                .Select(m => m.Conversation!.Id.Value),
            "conversation ends",
            items);
        AssertUniqueKeys(
            leaves.Where(m => m.Kind == ChatMessageKind.ConversationStart)
                .Select(m => m.Conversation!.Id.Value),
            "conversation starts",
            items);
    }

    private static string? FindFooterlessCard(ChatItems items)
    {
        // A suppressed footer must be supplied by the card's own block - one sitting elsewhere in the
        // list is a different band on screen, so "somewhere in the tree" is too weak a check here.
        var orphans = new List<ConversationId>();
        foreach (var item in items.Items) {
            var siblings = item is ExpandedConversationMessage block ? block.Items : [item];
            foreach (var card in siblings.OfType<ConversationMessage>()) {
                if (!card.HasSplitFooter)
                    continue;

                var conversationId = card.Conversation!.Id;
                var hasFooter = siblings.Any(m =>
                    m is ConversationFooter or LiveConversationFooter && m.Conversation!.Id == conversationId);
                if (!hasFooter)
                    orphans.Add(conversationId);
            }
        }

        return orphans.Count == 0
            ? null
            : "Card(s) suppressing a footer their block does not supply: "
                + $"{orphans.Select(id => id.Value).ToDelimitedString()}\n{Dump(items)}";
    }

    private static void AssertUniqueKeys(IEnumerable<string> keys, string scope, ChatItems items)
    {
        var duplicates = keys.GroupBy(k => k).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count == 0)
            return;

        throw new InvalidOperationException(
            $"Duplicate @key(s) in {scope}: {duplicates.ToDelimitedString()}\n{Dump(items)}");
    }

    private static string Dump(ChatItems items)
    {
        var sb = new StringBuilder();
        foreach (var item in items.Items) {
            sb.AppendLine($"{item.GetType().Name} #{item.Key} c={item.Conversation?.Id.Value ?? "-"}");
            if (item is IVirtualListGroup<ChatMessage> group)
                foreach (var child in group.Items)
                    sb.AppendLine(
                        $"    {child.GetType().Name} #{child.Key} c={child.Conversation?.Id.Value ?? "-"}");
            else if (item is IVirtualListGroup<ChatEntryMessage> entryGroup)
                foreach (var child in entryGroup.Items)
                    sb.AppendLine($"    {child.GetType().Name} #{child.Key}");
        }

        return sb.ToString();
    }

    // Joining now expands the live block, so a test about its collapsed form has to put it back - and
    // only once the expand has landed, since a toggle that races it is undone by the expand it preceded.
    private async Task CollapseJoinedLiveBlock(ChatUI chatUI, ChatId chatId, Conversation conversation)
    {
        var idRange = await Tester.Chats.GetIdRange(Tester.Session, chatId, CancellationToken.None);
        var query = new ChatDataQuery(idRange, -chatUI.HalfLoadLimit, chatUI.HalfLoadLimit);
        await ComputedTest.When(async ct => {
            await chatUI.GetChatItems(chatId, query, 0, ct);
            chatUI.IsConversationExpanded(conversation).Should().BeTrue();
        }, TimeSpan.FromSeconds(15));
        chatUI.ToggleExpandConversation(conversation.Id);
        await ComputedTest.When(async ct => {
            await chatUI.GetChatItems(chatId, query, 0, ct);
            chatUI.IsConversationExpanded(conversation).Should().BeFalse();
        }, TimeSpan.FromSeconds(15));
    }

    private async Task<Chat> CreateSettledChat(string title, bool isPublic = false)
    {
        // A fresh chat's "member added" entry is written by an async event handler, so a live session
        // latched right after creation can take V from a lid range that entry has not reached yet.
        // It then lands at V itself and stays visible there (it was never spoken), while every entry
        // the test creates sits one lid past where its V + n arithmetic puts it. Wait it out here.
        var (chat, _) = await Tester.CreateAndGetChat(isPublic, title);
        await ComputedTest.When(async ct => {
            var idRange = await Tester.Chats.GetIdRange(Tester.Session, chat.Id, ct);
            // Start is the lowest entry lid, and 0 while there is none - the range of an empty chat
            // is (0, 1), so its size can't tell an empty chat from one holding a single entry.
            idRange.Start.Should().BePositive("V must latch past the chat's opening system entry");
        }, TimeSpan.FromSeconds(10));
        return chat;
    }

    // An entry the call itself produced. CreateTextEntry alone makes a typed message, which the
    // hidden tail deliberately keeps visible - only what was spoken hides from a non-joined viewer.
    private async Task<ChatEntry> CreateSpokenEntry(ChatId chatId, string text)
    {
        var entry = await Tester.CreateTextEntry(chatId, text);
        return await Tester.Commander.Call(new ChatsBackend_ChangeEntry(
            entry.Id,
            entry.Version,
            Change.Update(new ChatEntryDiff {
                Audio = new ChatEntryAudio { MediaId = MediaId.Parse("fake:mediaid") },
            })));
    }

    private static List<long> LeafEntryLids(ChatItems items)
        => items.Items
            .SelectMany(i => i.GetLeafMessages())
            .OfType<ChatEntryMessage>()
            .Where(m => m.Kind == ChatMessageKind.None)
            .Select(m => m.Id)
            .ToList();
}
