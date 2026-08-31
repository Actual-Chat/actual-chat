using ActualChat.Testing.Host;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

[Collection(nameof(ChatUICollection))]
public sealed class ConversationCollapseTest(ChatAppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ChatAppHostFixture>(fixture, @out)
{
    private BlazorTester Tester => field ??= AppHost.NewBlazorTester(Out);

    protected override async Task DisposeAsync()
    {
        await Tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    [InlineData(60)]
    public async Task ShouldKeepChatTailWhenCollapsingTheLastConversation(int tailCount)
    {
        // arrange
        await Tester.SignInAsUniqueBob();
        var (chat, _) = await Tester.CreateAndGetChat(false, $"collapse-tail-test-{tailCount}");
        var entries = new List<ChatEntry>();
        for (var i = 0; i < 100; i++)
            entries.Add(await Tester.CreateTextEntry(chat.Id, $"m-{i}"));
        var firstConversationId = await Materialize(chat.Id, entries[0], entries[49]);
        var lastConversationId = await Materialize(chat.Id, entries[50], entries[99]);
        for (var i = 0; i < tailCount; i++)
            entries.Add(await Tester.CreateTextEntry(chat.Id, $"tail-{i}"));

        var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
        // Mirrors ChatView.GetChatDataQuery's "no-query+has-data+hasVeryLastItem" case: the view sits at
        // the tail, so the query is the visible range widened by HalfLoadLimit in both directions.
        var query = new ChatDataQuery(
            new Range<long>(entries[^10].LocalId, entries[^1].LocalId + 1),
            -chatUI.HalfLoadLimit,
            chatUI.HalfLoadLimit);

        var before = await chatUI.GetChatItems(chat.Id, query, 0, CancellationToken.None);
        Dump("expanded", before);
        before.HasAfter.Should().BeFalse("the view is at the chat's tail");

        // act
        chatUI.ToggleExpandConversation(lastConversationId);
        var afterLast = await chatUI.GetChatItems(chat.Id, query, 0, CancellationToken.None);
        Dump("last collapsed", afterLast);

        // assert
        afterLast.HasAfter.Should()
            .BeFalse("collapsing the last conversation must not unload the chat's tail");
        var lastLid = entries[^1].LocalId;
        afterLast.Items.SelectMany(i => i.GetLeafMessages())
            .Should().Contain(m => m.Id == lastLid || m.Id == lastConversationId.StartEntryLid);

        // act 2 - the control: a conversation that is NOT the last one
        chatUI.ToggleExpandConversation(firstConversationId);
        var afterFirst = await chatUI.GetChatItems(chat.Id, query, 0, CancellationToken.None);
        Dump("first collapsed too", afterFirst);
        afterFirst.HasAfter.Should().BeFalse("the tail is still the tail");

        // act 3 - re-expanding still has to load the conversation's own entries
        chatUI.ToggleExpandConversation(lastConversationId);
        var reExpanded = await chatUI.GetChatItems(chat.Id, query, 0, CancellationToken.None);
        Dump("last re-expanded", reExpanded);
        reExpanded.Items.SelectMany(i => i.GetLeafMessages())
            .Should().Contain(m => m.Id == entries[99].LocalId, "the expanded conversation must be loaded");
    }

    [Fact]
    public async Task ShouldKeepWitnessedEntriesExpandedWhenConversationMaterializes()
    {
        // arrange: the user is looking at the entries (VisibleLidRange covers them) before any
        // conversation exists over them
        await Tester.SignInAsUniqueBob();
        var (chat, _) = await Tester.CreateAndGetChat(false, "deferred-collapse");
        var (otherChat, _) = await Tester.CreateAndGetChat(false, "deferred-collapse-other");
        var entries = new List<ChatEntry>();
        for (var i = 0; i < 20; i++)
            entries.Add(await Tester.CreateTextEntry(chat.Id, $"m-{i}"));
        var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
        chatUI.SelectChatOnNavigation(chat.Id);
        var query = new ChatDataQuery(
            new Range<long>(entries[0].LocalId, entries[^1].LocalId + 1),
            -chatUI.HalfLoadLimit,
            chatUI.HalfLoadLimit) {
            VisibleLidRange = new Range<long>(entries[0].LocalId, entries[^1].LocalId + 1),
        };
        var before = await chatUI.GetChatItems(chat.Id, query, 0, CancellationToken.None);
        before.Items.SelectMany(i => i.GetLeafMessages())
            .Should().Contain(m => m.Id == entries[5].LocalId, "the entries are on screen pre-materialization");

        // act: a collapsed-by-default conversation materializes over the witnessed entries
        var conversationId = await Materialize(chat.Id, entries[0], entries[9], isExpandedByDefault: false);

        // assert: the entries stay rendered - the conversation is auto-expanded, not swallowed
        await ComputedTest.When(async ct => {
            var built = await chatUI.GetChatItems(chat.Id, query, 0, ct);
            built.Items.SelectMany(i => i.GetLeafMessages())
                .Should().Contain(m => m.Id == entries[5].LocalId, "witnessed entries must not collapse in place");
            chatUI.AutoExpandedConversations.Value.Should().Contain(conversationId);
        }, TimeSpan.FromSeconds(10));

        // act: leave the chat and come back
        chatUI.SelectChatOnNavigation(otherChat.Id);
        chatUI.SelectChatOnNavigation(chat.Id);
        var fresh = await chatUI.GetChatItems(chat.Id, query, 0, CancellationToken.None);

        // assert: the conversation now renders collapsed - its entries are gone, its card is there
        fresh.Items.SelectMany(i => i.GetLeafMessages())
            .Should().NotContain(m => m.Id == entries[5].LocalId, "a fresh visit renders per tier");
        fresh.Items.SelectMany(i => i.GetLeafMessages())
            .Should().Contain(m => m is ConversationMessage && m.Id == conversationId.StartEntryLid);

        // assert: and it stays collapsed - a collapsed conversation emits no entries, so no rebuild
        // can re-witness the lids it covers and auto-expand it again
        var rebuiltFresh = await chatUI.GetChatItems(chat.Id, query, 0, CancellationToken.None);
        rebuiltFresh.Items.SelectMany(i => i.GetLeafMessages())
            .Should().NotContain(m => m.Id == entries[5].LocalId,
                "the conversation stays collapsed across rebuilds on a fresh visit");
    }

    [Fact]
    public async Task ShouldNotWitnessEntriesCoveredByCollapsedConversation()
    {
        // arrange: the conversation exists (collapsed) BEFORE the first build ever runs
        await Tester.SignInAsUniqueBob();
        var (chat, _) = await Tester.CreateAndGetChat(false, "deferred-collapse-pre");
        var entries = new List<ChatEntry>();
        for (var i = 0; i < 20; i++)
            entries.Add(await Tester.CreateTextEntry(chat.Id, $"m-{i}"));
        var conversationId = await Materialize(chat.Id, entries[0], entries[9], isExpandedByDefault: false);
        var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
        chatUI.SelectChatOnNavigation(chat.Id);
        var query = new ChatDataQuery(
            new Range<long>(entries[0].LocalId, entries[^1].LocalId + 1),
            -chatUI.HalfLoadLimit,
            chatUI.HalfLoadLimit) {
            VisibleLidRange = new Range<long>(entries[0].LocalId, entries[^1].LocalId + 1),
        };

        // act: two builds - the first could only witness the visible tail, never the covered lids
        await chatUI.GetChatItems(chat.Id, query, 0, CancellationToken.None);
        var rebuilt = await chatUI.GetChatItems(chat.Id, query, 0, CancellationToken.None);

        // assert: the conversation stays collapsed and never enters the auto set
        rebuilt.Items.SelectMany(i => i.GetLeafMessages())
            .Should().NotContain(m => m.Id == entries[5].LocalId, "covered lids were never witnessed");
        chatUI.AutoExpandedConversations.Value.Should().NotContain(conversationId);
    }

    [Fact]
    public async Task ShouldKeepManualCollapseOfAutoExpandedConversation()
    {
        // arrange: same witnessed setup, conversation auto-expands
        await Tester.SignInAsUniqueBob();
        var (chat, _) = await Tester.CreateAndGetChat(false, "deferred-collapse-manual");
        var entries = new List<ChatEntry>();
        for (var i = 0; i < 20; i++)
            entries.Add(await Tester.CreateTextEntry(chat.Id, $"m-{i}"));
        var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
        chatUI.SelectChatOnNavigation(chat.Id);
        var query = new ChatDataQuery(
            new Range<long>(entries[0].LocalId, entries[^1].LocalId + 1),
            -chatUI.HalfLoadLimit,
            chatUI.HalfLoadLimit) {
            VisibleLidRange = new Range<long>(entries[0].LocalId, entries[^1].LocalId + 1),
        };
        await chatUI.GetChatItems(chat.Id, query, 0, CancellationToken.None); // witnesses the entries
        var conversationId = await Materialize(chat.Id, entries[0], entries[9], isExpandedByDefault: false);
        await ComputedTest.When(async ct => {
            var built = await chatUI.GetChatItems(chat.Id, query, 0, ct);
            built.Items.SelectMany(i => i.GetLeafMessages())
                .Should().Contain(m => m.Id == entries[5].LocalId, "witnessed entries must not collapse in place");
            chatUI.AutoExpandedConversations.Value.Should().Contain(conversationId);
        }, TimeSpan.FromSeconds(10));

        // act: the user collapses it by hand
        chatUI.ToggleExpandConversation(conversationId);
        var collapsed = await chatUI.GetChatItems(chat.Id, query, 0, CancellationToken.None);

        // assert: collapsed, and it stays collapsed on the next rebuild (no auto re-add)
        collapsed.Items.SelectMany(i => i.GetLeafMessages())
            .Should().NotContain(m => m.Id == entries[5].LocalId, "a manual collapse must win over auto-expansion");
        var rebuilt = await chatUI.GetChatItems(chat.Id, query, 0, CancellationToken.None);
        rebuilt.Items.SelectMany(i => i.GetLeafMessages())
            .Should().NotContain(m => m.Id == entries[5].LocalId, "suppression must survive rebuilds");
    }

    // Private methods

    private async Task<ConversationId> Materialize(
        ChatId chatId, ChatEntry first, ChatEntry last, bool isExpandedByDefault = true)
    {
        var id = ConversationId.New(chatId, first.LocalId);
        var conversation = new Conversation(id) {
            Title = $"Recap {first.LocalId}",
            Summary = "s",
            Description = "d",
            EndEntryLid = last.LocalId,
            MessageCount = (int)(last.LocalId - first.LocalId + 1),
            IsExpandedByDefault = isExpandedByDefault,
        };
        await Tester.Commander.Call(new ConversationBackend_Materialize(conversation), CancellationToken.None);
        return id;
    }

    private void Dump(string title, ChatItems items)
    {
        var keys = items.Items
            .Select(i => i is ExpandedConversationMessage block
                ? $"[{block.Items.Select(x => ((IVirtualListItem)x).Key).ToDelimitedString(" ")}]"
                : ((IVirtualListItem)i).Key)
            .ToList();
        Out.WriteLine($"--- {title}: hasBefore={items.HasBefore}, hasAfter={items.HasAfter}, "
            + $"{items.Items.Count} items, {items.Items.Sum(i => i.GetLeafMessages().Count())} rows");
        Out.WriteLine(string.Join(", ", keys));
    }
}
