using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class AutoExpansionRuleTest
{
    private static readonly ChatId TestChatId = ChatId.Parse("aaaaaaaaaaaaaaaaaaaa");
    private static readonly IImmutableSet<ConversationId> Empty = ImmutableHashSet<ConversationId>.Empty;

    [Fact]
    public void ShouldAutoExpandCollapsedConversationOverWitnessedLids()
    {
        // arrange
        var witnessed = new LidRangeSet();
        witnessed.Add(new Range<long>(100, 110));
        var range = new Range<long>(95, 120);

        // act
        var result = ChatUI.GetNewAutoExpansions(
            TestChatId, [range], Empty, Empty, Empty,
            _ => false, witnessed, null, null);

        // assert
        result.Should().Equal(ConversationId.New(TestChatId, 95));
    }

    [Fact]
    public void ShouldSkipExpandedSuppressedAndNonWitnessedConversations()
    {
        // arrange
        var witnessed = new LidRangeSet();
        witnessed.Add(new Range<long>(100, 110));
        var witnessedRange = new Range<long>(95, 120);
        var farRange = new Range<long>(500, 600);
        var witnessedId = ConversationId.New(TestChatId, 95);

        // act
        var whenDefaultExpanded = ChatUI.GetNewAutoExpansions(
            TestChatId, [witnessedRange, farRange], ImmutableHashSet.Create(witnessedId), Empty, Empty,
            _ => false, witnessed, null, null);
        var whenExpandedViaOverride = ChatUI.GetNewAutoExpansions(
            TestChatId, [witnessedRange, farRange], Empty, ImmutableHashSet.Create(witnessedId), Empty,
            _ => false, witnessed, null, null);
        var whenSuppressed = ChatUI.GetNewAutoExpansions(
            TestChatId, [witnessedRange, farRange], Empty, Empty, Empty,
            id => id == witnessedId, witnessed, null, null);
        var whenAlreadyAutoExpanded = ChatUI.GetNewAutoExpansions(
            TestChatId, [witnessedRange, farRange], Empty, Empty, ImmutableHashSet.Create(witnessedId),
            _ => false, witnessed, null, null);

        // assert
        whenDefaultExpanded.Should().BeEmpty();
        whenExpandedViaOverride.Should().BeEmpty();
        whenSuppressed.Should().BeEmpty();
        whenAlreadyAutoExpanded.Should().BeEmpty();
    }

    [Fact]
    public void ShouldSkipLiveAndMaterializedBlockIds()
    {
        // arrange
        var witnessed = new LidRangeSet();
        witnessed.Add(new Range<long>(100, 110));
        var range = new Range<long>(95, 120);
        var id = ConversationId.New(TestChatId, 95);

        // act
        var whenLiveBlock = ChatUI.GetNewAutoExpansions(
            TestChatId, [range], Empty, Empty, Empty,
            _ => false, witnessed, id, null);
        var whenMaterializedBlock = ChatUI.GetNewAutoExpansions(
            TestChatId, [range], Empty, Empty, Empty,
            _ => false, witnessed, null, id);

        // assert
        whenLiveBlock.Should().BeEmpty();
        whenMaterializedBlock.Should().BeEmpty();
    }

    [Fact]
    public void ShouldReTriggerWhenRangeGrowsOverWitnessedLids()
    {
        // arrange
        var witnessed = new LidRangeSet();
        witnessed.Add(new Range<long>(100, 110));
        var grownRange = new Range<long>(50, 105);
        var previouslyAutoExpanded = ImmutableHashSet.Create(ConversationId.New(TestChatId, 95));

        // act
        var result = ChatUI.GetNewAutoExpansions(
            TestChatId, [grownRange], Empty, Empty, previouslyAutoExpanded,
            _ => false, witnessed, null, null);

        // assert
        result.Should().Equal(ConversationId.New(TestChatId, 50));
    }

    [Fact]
    public void ShouldEvaluateEachRangeIndependently()
    {
        // arrange
        var witnessed = new LidRangeSet();
        witnessed.Add(new Range<long>(100, 110));
        witnessed.Add(new Range<long>(200, 210));
        var suppressedRange = new Range<long>(95, 120);
        var qualifyingRange = new Range<long>(195, 220);
        var farRange = new Range<long>(500, 600);
        var suppressedId = ConversationId.New(TestChatId, 95);

        // act
        var result = ChatUI.GetNewAutoExpansions(
            TestChatId, [suppressedRange, qualifyingRange, farRange], Empty, Empty, Empty,
            id => id == suppressedId, witnessed, null, null);

        // assert
        result.Should().Equal(ConversationId.New(TestChatId, 195));
    }
}
