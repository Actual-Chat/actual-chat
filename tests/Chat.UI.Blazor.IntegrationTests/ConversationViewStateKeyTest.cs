using System.Collections.Immutable;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

/// <summary>
/// Pins <see cref="ConversationViewState"/> value equality: it is a compute-method argument of
/// ChatUI.GetTile, so it is the tile cache key. Reference equality there re-keys every tile on
/// every rebuild, turning the tile cache into pure garbage.
/// </summary>
public sealed class ConversationViewStateKeyTest
{
    [Fact]
    public void StructurallyIdenticalStatesMustBeEqual()
    {
        // arrange
        var chatId = ChatId.Parse("the-actual-one");
        var expanded = ConversationId.New(chatId, 100);
        IImmutableSet<ConversationId> setA = ImmutableHashSet.Create(expanded);
        IImmutableSet<ConversationId> setB = ImmutableHashSet.Create(expanded);

        // act
        var a = new ConversationViewState(true, setA, new Range<long>(1, 5), expanded, new Range<long>(1, 5), null);
        var b = new ConversationViewState(true, setB, new Range<long>(1, 5), expanded, new Range<long>(1, 5), null);

        // assert
        setA.Should().NotBeSameAs(setB, "distinct instances are required or this test is vacuous");
        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }
}
