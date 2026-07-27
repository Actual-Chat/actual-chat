using ActualChat.Testing.Host;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Components;

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
    public async Task CollapsingTheLastConversationKeepsTheChatTail(int tailCount)
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

    // Private methods

    private async Task<ConversationId> Materialize(ChatId chatId, ChatEntry first, ChatEntry last)
    {
        var id = ConversationId.New(chatId, first.LocalId);
        var conversation = new Conversation(id) {
            Title = $"Recap {first.LocalId}",
            Summary = "s",
            Description = "d",
            EndEntryLid = last.LocalId,
            MessageCount = (int)(last.LocalId - first.LocalId + 1),
            IsExpandedByDefault = true,
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
