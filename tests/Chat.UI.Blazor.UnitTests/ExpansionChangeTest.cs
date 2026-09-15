using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class ExpansionChangeTest
{
    private static readonly ChatId ChatA = ChatId.Parse("aaaaaaaaaaaaaaaaaaaa");
    private static readonly ChatId ChatB = ChatId.Parse("bbbbbbbbbbbbbbbbbbbb");
    private static readonly IImmutableSet<ConversationId> Empty = ImmutableHashSet<ConversationId>.Empty;

    [Fact]
    public void ShouldIgnoreConversationsOfOtherChats()
    {
        // arrange: another chat's navigation flipped an override for a conversation at a lower lid
        var overrides = ImmutableHashSet.Create(ConversationId.New(ChatB, 3175), ConversationId.New(ChatA, 9000));
        var autoExpanded = ImmutableHashSet.Create(ConversationId.New(ChatB, 100));

        // act
        var result = ChatUI.GetChangedExpansions(ChatA, overrides, autoExpanded, Empty, Empty);

        // assert
        result.Should().Equal(
            [ConversationId.New(ChatA, 9000)],
            because: "a foreign conversation's lid must never widen this chat's load window");
    }

    [Fact]
    public void ShouldBeEmptyWhenOnlyOtherChatsChanged()
    {
        // arrange
        var lastOverrides = ImmutableHashSet.Create(ConversationId.New(ChatA, 500));
        var overrides = lastOverrides.Add(ConversationId.New(ChatB, 3175));

        // act
        var result = ChatUI.GetChangedExpansions(ChatA, overrides, Empty, lastOverrides, Empty);

        // assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void ShouldReportAdditionsAndRemovalsInBothSetsOrderedByLid()
    {
        // arrange
        var lastOverrides = ImmutableHashSet.Create(ConversationId.New(ChatA, 300));
        var autoExpanded = ImmutableHashSet.Create(ConversationId.New(ChatA, 200));
        var lastAutoExpanded = ImmutableHashSet.Create(ConversationId.New(ChatA, 400));
        var overrides = ImmutableHashSet.Create(ConversationId.New(ChatA, 100));

        // act
        var result = ChatUI.GetChangedExpansions(ChatA, overrides, autoExpanded, lastOverrides, lastAutoExpanded);

        // assert
        result.Should().Equal(
            ConversationId.New(ChatA, 100),
            ConversationId.New(ChatA, 200),
            ConversationId.New(ChatA, 300),
            ConversationId.New(ChatA, 400));
    }

    [Fact]
    public void ShouldBeEmptyWhenNothingChanged()
    {
        // arrange
        var overrides = ImmutableHashSet.Create(ConversationId.New(ChatA, 100), ConversationId.New(ChatB, 50));
        var autoExpanded = ImmutableHashSet.Create(ConversationId.New(ChatA, 200));

        // act
        var result = ChatUI.GetChangedExpansions(ChatA, overrides, autoExpanded, overrides, autoExpanded);

        // assert
        result.Should().BeEmpty();
    }
}
