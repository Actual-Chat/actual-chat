namespace ActualChat.Core.UnitTests.Identifiers;

public class PartitionKeyTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Theory]
    [InlineData(0, "000000")]
    [InlineData(-1, "ffffff")]
    [InlineData(0x12345678, "345678")]
    public void ValueShouldKeepOnlySixHexDigits(int value, string expected)
    {
        // act
        var key = new PartitionKey(value);

        // assert
        key.ToString().Should().Be(expected);
        key.Value.Should().Be(value & 0xffffff);
        PartitionKey.Parse(expected).Should().Be(key);
        key.AssertPassesThroughSerializers(Out);
    }

    [Fact]
    public void PrefixesAndSlicesShouldPreserveTheirPosition()
    {
        // arrange
        var key = new PartitionKey(0xabcdef);

        // act
        var prefix = key.ToString(2);
        var middle = key.ToString(2, 2);

        // assert
        prefix.Should().Be("ab");
        middle.Should().Be("cd");
        key.ToString(2..4).Should().Be(middle);
        key.ToString(^2..).Should().Be("ef");
        key.GetValue(1).Should().Be(0xa);
        key.GetValue(2).Should().Be(0xab);
        PartitionKey.Parse(prefix).Value.Should().Be(0xab0000);
        PartitionKey.Parse(middle, 2).Value.Should().Be(0x00cd00);
        PartitionKey.Parse("ABCDEF").Should().Be(key);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1234567")]
    [InlineData("-1")]
    [InlineData(" ab")]
    [InlineData("ab ")]
    [InlineData("0x12")]
    [InlineData("xyz")]
    public void InvalidTextShouldBeRejected(string? value)
    {
        // act
        var success = PartitionKey.TryParse(value, out var key);

        // assert
        success.Should().BeFalse();
        key.Should().Be(default);
        FluentActions.Invoking(() => PartitionKey.Parse(value)).Should().Throw<FormatException>();
    }

    [Fact]
    public void InvalidSlicesShouldBeRejected()
    {
        // arrange
        var key = new PartitionKey(0xabcdef);

        // act, assert
        FluentActions.Invoking(() => key.ToString(-1, 2)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => key.ToString(0, 7)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => key.ToString(5, 2)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => PartitionKey.Parse("ab", 5)).Should().Throw<FormatException>();
    }

    [Fact]
    public void TypedAndUntypedIdsShouldUseTheSameStablePartition()
    {
        // arrange
        var userId = UserId.Parse("abcdef");
        var nodeRef = NodeRef.Parse("abcdef");

        // act
        var key = userId.PartitionKey;

        // assert
        key.Should().Be(PartitionKey.New("abcdef"));
        key.Value.Should().Be("abcdef".GetXxHash3() & 0xffffff);
        userId.TypedId.PartitionKey.Should().Be(key);
        nodeRef.PartitionKey.Should().Be(key);
        ((ISymbolIdentifier)nodeRef).PartitionKey.Should().Be(key);
        ((IObjectId)userId).PartitionKey.Should().Be(key);
        ((IObjectId)userId.TypedId).PartitionKey.Should().Be(key);
    }

    [Fact]
    public void StableHashShouldMatchTheSharedReferenceVector()
    {
        // act
        var key = PartitionKey.New("abc");

        // assert
        key.Value.Should().Be(0x994fea);
        key.ToString().Should().Be("994fea");
        UserId.Parse("abc").PartitionKey.Should().Be(key);
    }

    [Fact]
    public void EveryHexSliceShouldRoundTripAtItsOriginalOffset()
    {
        // arrange
        const string value = "abcdef";
        var key = PartitionKey.Parse(value);

        // act, assert
        for (var start = 0; start < 6; start++)
            for (var length = 1; length <= 6 - start; length++) {
                var slice = key.ToString(start, length);
                slice.Should().Be(value.Substring(start, length));
                var parsed = PartitionKey.Parse(slice, start);
                parsed.ToString().Should().Be(slice.PadLeft(start + length, '0').PadRight(6, '0'));
            }
    }
}
