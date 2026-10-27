using ActualChat.Comparison;

namespace ActualChat.Core.UnitTests.Comparison;

public sealed class SerializedEqualityComparerTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void CopiesWithTheSameContentShouldBeEqual()
    {
        // arrange
        var x = new ByRefRecord("a", 1);
        var y = new ByRefRecord("a", 1);
        var comparer = SerializedEqualityComparer<ByRefRecord>.Instance;

        // act
        var isEqual = comparer.Equals(x, y);

        // assert
        x.Equals(y).Should().BeFalse("the record compares by reference, which is what the comparer exists for");
        isEqual.Should().BeTrue();
        comparer.GetHashCode(x).Should().Be(comparer.GetHashCode(y));
    }

    [Fact]
    public void DifferentContentOrNullShouldNotBeEqual()
    {
        // arrange
        var x = new ByRefRecord("a", 1);
        var comparer = SerializedEqualityComparer<ByRefRecord>.Instance;

        // act
        var isEqualToChanged = comparer.Equals(x, x with { Count = 2 });
        var isEqualToNull = comparer.Equals(x, null);

        // assert
        isEqualToChanged.Should().BeFalse();
        isEqualToNull.Should().BeFalse();
        comparer.Equals(null, null).Should().BeTrue();
    }

    // Nested types

    [DataContract, MessagePackObject]
    public sealed partial record ByRefRecord(
        [property: DataMember, Key(0)] string Name,
        [property: DataMember, Key(1)] int Count)
    {
        public bool Equals(ByRefRecord? other) => ReferenceEquals(this, other);
        public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
    }
}
