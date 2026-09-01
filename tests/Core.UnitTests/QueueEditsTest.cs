using ActualChat.Messaging;

namespace ActualChat.Core.UnitTests;

public sealed class QueueEditsTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void EditsShouldReplaceInPlaceRemoveAndAppend()
    {
        // arrange
        var a = new Item(1);
        var b = new Item(2);
        var c = new Item(3);
        var list = new List<Item> { a, b, c };
        var a2 = new Item(11);
        var d = new Item(4);

        // act
        new QueueEdits<Item>()
            .Replace(a, a2)
            .Remove(b)
            .Add(d)
            .ApplyTo(list);

        // assert
        list.Should().Equal([a2, c, d], because: "a replacement keeps its position and an add goes to the tail");
    }

    [Fact]
    public void ReplaceShouldUseReferenceEqualityNotValueEquality()
    {
        // arrange
        var a = new Item(1);
        var aClone = new Item(1);
        var list = new List<Item> { a };
        var replacement = new Item(9);

        // act
        new QueueEdits<Item>().Replace(aClone, replacement).ApplyTo(list);

        // assert
        list.Should().Equal([a], because: "aClone isn't in the list by reference");
    }

    [Fact]
    public void MissingReplaceOrRemoveTargetShouldBeNoOp()
    {
        // arrange
        var a = new Item(1);
        var list = new List<Item> { a };
        var ghost = new Item(2);

        // act
        new QueueEdits<Item>().Remove(ghost).Replace(ghost, new Item(3)).ApplyTo(list);

        // assert
        list.Should().Equal([a], because: "edits targeting an absent item are ignored");
    }

    // Nested types

    private sealed record Item(int Value);
}
