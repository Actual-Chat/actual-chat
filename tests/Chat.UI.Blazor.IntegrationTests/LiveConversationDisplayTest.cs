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

    [Fact]
    public async Task LeaveKeepsRenderedTailVisible()
    {
        // Hanging up must freeze the render, not collapse it - a reader mid-scroll must not jump.

        // arrange
        await Tester.SignInAsUniqueBob();
        var (chat, _) = await Tester.CreateAndGetChat(false, "leave-freeze-test");
        var author = await Tester.GetOwnAuthor(chat.Id).Require();
        var peerId = AuthorId.New(chat.Id, 777_100);
        var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
        await liveBackend.OnStreamRegistered(chat.Id, author.Id, null, true, CancellationToken.None);
        await liveBackend.OnStreamRegistered(chat.Id, peerId, null, true, CancellationToken.None);
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
            items.Items.OfType<ExpandedConversationMessage>().Should().ContainSingle();
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task NewEntriesAfterLeaveStayHidden()
    {
        // The freeze must hold going forward too - a message posted after hang-up can't sneak in.

        // arrange
        await Tester.SignInAsUniqueBob();
        var (chat, _) = await Tester.CreateAndGetChat(false, "leave-freeze-new-entries-test");
        var author = await Tester.GetOwnAuthor(chat.Id).Require();
        var peerId = AuthorId.New(chat.Id, 777_110);
        var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
        await liveBackend.OnStreamRegistered(chat.Id, author.Id, null, true, CancellationToken.None);
        await liveBackend.OnStreamRegistered(chat.Id, peerId, null, true, CancellationToken.None);
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
        await Tester.CreateTextEntry(chat.Id, "posted after leave");

        // assert (sustained - the new entry's lid must never surface)
        await Task.Delay(1000);
        var items2 = await chatUI.GetChatItems(chat.Id, query, 0, CancellationToken.None);
        LeafEntryLids(items2).Should().Equal(frozenLeafLids);
    }

    [Fact]
    public async Task CloseKeepsRenderedItemsAndKey()
    {
        // Closing the call must not disturb a viewer who was watching it live - same rows, same @key.

        // arrange
        await Tester.SignInAsUniqueBob();
        var (chat, _) = await Tester.CreateAndGetChat(false, "close-freeze-test");
        var author = await Tester.GetOwnAuthor(chat.Id).Require();
        var peerId = AuthorId.New(chat.Id, 777_120);
        var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
        await liveBackend.OnStreamRegistered(chat.Id, author.Id, null, true, CancellationToken.None);
        await liveBackend.OnStreamRegistered(chat.Id, peerId, null, true, CancellationToken.None);
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
        }, TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task CloseWithPreLatchContextKeepsFrozenTailVisible()
    {
        // When the summary's context reaches back before V, the persisted (materialized) conversation
        // starts at ContextStartLid, not V - id-tile loading must resolve that identity to the same
        // governed fold range GetTile uses, or the block's frozen (unfolded) tail loses its id-tiles
        // and goes missing after close. A second, un-lagged summary widens the raw range past the
        // governed fold boundary so the fold and the persisted range genuinely diverge - the exact
        // condition that exposed the bug.

        // arrange
        await Tester.SignInAsUniqueBob();
        var (chat, _) = await Tester.CreateAndGetChat(false, "close-context-test");
        var context0 = await Tester.CreateTextEntry(chat.Id, "context-0");
        await Tester.CreateTextEntry(chat.Id, "context-1");
        var author = await Tester.GetOwnAuthor(chat.Id).Require();
        var peerId = AuthorId.New(chat.Id, 777_160);
        var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
        await liveBackend.OnStreamRegistered(chat.Id, author.Id, null, true, CancellationToken.None);
        await liveBackend.OnStreamRegistered(chat.Id, peerId, null, true, CancellationToken.None);
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

        // A second summary widens EndEntryLid to cover the tail, but the default (un-shrunk) FoldLag
        // keeps the governed fold boundary at V+3 - so at close, the fold range [V, V+3) is genuinely
        // narrower than the persisted range [ContextStartLid, V+6).
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
        var (chat, _) = await Tester.CreateAndGetChat(false, "close-toggle-test");
        var author = await Tester.GetOwnAuthor(chat.Id).Require();
        var peerId = AuthorId.New(chat.Id, 777_130);
        var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
        await liveBackend.OnStreamRegistered(chat.Id, author.Id, null, true, CancellationToken.None);
        await liveBackend.OnStreamRegistered(chat.Id, peerId, null, true, CancellationToken.None);
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
    public async Task FoldLagDefersMidCallFold()
    {
        // A fold must not land the instant its summary does - the lag gives the reader a beat
        // before the entries it just covered disappear behind the card.

        // arrange
        await Tester.SignInAsUniqueBob();
        var (chat, _) = await Tester.CreateAndGetChat(false, "fold-lag-test");
        var author = await Tester.GetOwnAuthor(chat.Id).Require();
        var peerId = AuthorId.New(chat.Id, 777_140);
        var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
        await liveBackend.OnStreamRegistered(chat.Id, author.Id, null, true, CancellationToken.None);
        await liveBackend.OnStreamRegistered(chat.Id, peerId, null, true, CancellationToken.None);
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

        var liveBlockUI = Tester.ScopedAppServices.GetRequiredService<LiveBlockUI>();
        liveBlockUI.FoldLag = TimeSpan.FromSeconds(2);
        var chatAudioUI = Tester.ScopedAppServices.GetRequiredService<ChatAudioUI>();
        var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
        await chatAudioUI.SetListeningState(chat.Id, true);
        chatUI.SelectChatOnNavigation(chat.Id);
        var idRange = await Tester.Chats.GetIdRange(Tester.Session, chat.Id, CancellationToken.None);
        var query = new ChatDataQuery(idRange, -chatUI.HalfLoadLimit, chatUI.HalfLoadLimit);
        List<long> beforeLids = null!;
        await ComputedTest.When(async ct => {
            var items = await chatUI.GetChatItems(chat.Id, query, 0, ct);
            var lids = LeafEntryLids(items);
            lids.Should().Contain(v + 3);
            lids.Should().Contain(v + 4);
            beforeLids = lids;
        }, TimeSpan.FromSeconds(10));

        // act - a summary pass advances coverage over the two new entries
        await liveBackend.UpdateSummary(chat.Id,
            new LiveSessionSummary {
                Title = "Latch", Description = "d2", Summary = "s2",
                EndEntryLid = v + 4, MessageCount = 5,
            }, CancellationToken.None);

        // assert (sustained - nothing folds within the lag window)
        await Task.Delay(700);
        LeafEntryLids(await chatUI.GetChatItems(chat.Id, query, 0, CancellationToken.None)).Should().Equal(beforeLids);

        // assert - after the lag the two newly covered entries fold
        await ComputedTest.When(async ct => {
            var lids = LeafEntryLids(await chatUI.GetChatItems(chat.Id, query, 0, ct));
            lids.Should().NotContain(v + 3);
            lids.Should().NotContain(v + 4);
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task ViewportGuardDefersFoldWhileVisible()
    {
        // The fold must defer while its entries are on screen and complete once they scroll away -
        // even with zero lag, a visible entry can't vanish out from under the reader.

        // arrange
        await Tester.SignInAsUniqueBob();
        var (chat, _) = await Tester.CreateAndGetChat(false, "viewport-guard-test");
        var author = await Tester.GetOwnAuthor(chat.Id).Require();
        var peerId = AuthorId.New(chat.Id, 777_150);
        var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
        await liveBackend.OnStreamRegistered(chat.Id, author.Id, null, true, CancellationToken.None);
        await liveBackend.OnStreamRegistered(chat.Id, peerId, null, true, CancellationToken.None);
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

        var liveBlockUI = Tester.ScopedAppServices.GetRequiredService<LiveBlockUI>();
        liveBlockUI.FoldLag = TimeSpan.Zero;
        var chatAudioUI = Tester.ScopedAppServices.GetRequiredService<ChatAudioUI>();
        var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
        await chatAudioUI.SetListeningState(chat.Id, true);
        chatUI.SelectChatOnNavigation(chat.Id);
        var idRange = await Tester.Chats.GetIdRange(Tester.Session, chat.Id, CancellationToken.None);
        var query = new ChatDataQuery(idRange, -chatUI.HalfLoadLimit, chatUI.HalfLoadLimit);
        List<long> beforeLids = null!;
        await ComputedTest.When(async ct => {
            var items = await chatUI.GetChatItems(chat.Id, query, 0, ct);
            var lids = LeafEntryLids(items);
            lids.Should().Contain(v + 3);
            lids.Should().Contain(v + 4);
            beforeLids = lids;
        }, TimeSpan.FromSeconds(10));
        chatUI.ItemVisibility.Value = new ChatViewItemVisibility(
            chat.Id,
            new HashSet<ChatMessageKey> {
                ChatMessageKey.New(ChatMessageKind.None, v + 3),
                ChatMessageKey.New(ChatMessageKind.None, v + 4),
            },
            false);

        // act 1 - a summary pass covers the visible entries, but the guard holds the fold
        await liveBackend.UpdateSummary(chat.Id,
            new LiveSessionSummary {
                Title = "Latch", Description = "d2", Summary = "s2",
                EndEntryLid = v + 4, MessageCount = 5,
            }, CancellationToken.None);
        await Task.Delay(700);
        LeafEntryLids(await chatUI.GetChatItems(chat.Id, query, 0, CancellationToken.None)).Should().Equal(beforeLids);

        // act 2 - the entries scroll out of view, so the fold completes
        chatUI.ItemVisibility.Value = ChatViewItemVisibility.Empty;

        // assert
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
        var (chat, _) = await Tester.CreateAndGetChat(false, "leave-reactive-test");
        var author = await Tester.GetOwnAuthor(chat.Id).Require();
        var peerId = AuthorId.New(chat.Id, 777_170);
        var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
        await liveBackend.OnStreamRegistered(chat.Id, author.Id, null, true, CancellationToken.None);
        await liveBackend.OnStreamRegistered(chat.Id, peerId, null, true, CancellationToken.None);
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

    // Private methods

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

    private static List<long> LeafEntryLids(ChatItems items)
        => items.Items
            .SelectMany(i => i.GetLeafMessages())
            .OfType<ChatEntryMessage>()
            .Where(m => m.Kind == ChatMessageKind.None)
            .Select(m => m.Id)
            .ToList();
}
