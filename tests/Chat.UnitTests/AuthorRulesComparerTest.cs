namespace ActualChat.Chat.UnitTests;

public sealed class AuthorRulesComparerTest
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ComparerShouldObserveIntroductionState(bool isPeerContactPending, bool hasReachedPeerMessageLimit)
    {
        // arrange
        var rules = new AuthorRules(ChatId.Parse("the-actual-one"), null, null, ChatPermissions.Write);
        var updated = rules with {
            IsPeerContactPending = isPeerContactPending,
            HasReachedPeerMessageLimit = hasReachedPeerMessageLimit,
        };
        var comparer = new AuthorRulesComparer();

        // act
        var isEqual = comparer.Equals(rules, updated);

        // assert
        isEqual.Should().Be(!isPeerContactPending && !hasReachedPeerMessageLimit);
        if (isEqual)
            comparer.GetHashCode(updated).Should().Be(comparer.GetHashCode(rules));
    }
}
