namespace ActualChat.Core.UnitTests.Identifiers;

public class ShardKeyTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Theory]
    [InlineData(0u, "00000000")]
    [InlineData(uint.MaxValue, "ffffffff")]
    [InlineData(0x12345678u, "12345678")]
    [InlineData(0x80000000u, "80000000")]
    public void ValueShouldKeepAllThirtyTwoBits(uint value, string expected)
    {
        // act
        var key = new ShardKey(value);

        // assert
        key.ToString().Should().Be(expected);
        key.Value.Should().Be(value);
        ShardKey.Parse(expected).Should().Be(key);
        key.GetValue(8).Should().Be(value);
        key.AssertPassesThroughSerializers(Out);
    }

    [Fact]
    public void PrefixesAndSlicesShouldPreserveTheirPosition()
    {
        // arrange
        var key = new ShardKey(0xabcdef12);

        // act
        var prefix = key.ToString(2);
        var middle = key.ToString(2, 2);

        // assert
        prefix.Should().Be("ab");
        middle.Should().Be("cd");
        key.ToString(2..4).Should().Be(middle);
        key.ToString(^2..).Should().Be("12");
        key.GetValue(1).Should().Be(0xau);
        key.GetValue(2).Should().Be(0xabu);
        ShardKey.Parse(prefix).Value.Should().Be(0xab000000u);
        ShardKey.Parse(middle, 2).Value.Should().Be(0x00cd0000u);
        ShardKey.Parse("ABCDEF12").Should().Be(key);
        key.GetValue(8, 0).Should().Be(0u);
        key.ToString(8, 0).Should().Be("");
    }

    [Fact]
    public void ShortHexStringsShouldBeReused()
    {
        // act, assert
        for (var value = 0u; value < 256; value++) {
            var key = new ShardKey(value << 24);
            var otherKey = new ShardKey((value << 24) | 0xffffff);
            key.ToString(1).Should().BeSameAs(otherKey.ToString(1));
            key.ToString(2).Should().BeSameAs(otherKey.ToString(2));
            key.ToString(2).Should().Be(value.ToString("x2"));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("123456789")]
    [InlineData("-1")]
    [InlineData(" ab")]
    [InlineData("ab ")]
    [InlineData("0x12")]
    [InlineData("xyz")]
    public void InvalidTextShouldBeRejected(string? value)
    {
        // act
        var success = ShardKey.TryParse(value, out var key);

        // assert
        success.Should().BeFalse();
        key.Should().Be(default);
        FluentActions.Invoking(() => ShardKey.Parse(value)).Should().Throw<FormatException>();
    }

    [Fact]
    public void InvalidSlicesShouldBeRejected()
    {
        // arrange
        var key = new ShardKey(0xabcdef12);

        // act, assert
        FluentActions.Invoking(() => key.ToString(-1, 2)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => key.ToString(0, 9)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => key.ToString(7, 2)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => ShardKey.Parse("ab", 7)).Should().Throw<FormatException>();
    }

    [Fact]
    public void TypedAndUntypedIdsShouldUseTheSameStableShardKey()
    {
        // arrange
        var userId = UserId.Parse("abcdef");
        var nodeRef = NodeRef.Parse("abcdef");

        // act
        var key = userId.ShardKey;

        // assert
        key.Should().Be(ShardKey.New("abcdef"));
        key.Value.Should().Be(unchecked((uint)"abcdef".GetXxHash3()));
        userId.TypedId.ShardKey.Should().Be(key);
        nodeRef.ShardKey.Should().Be(key);
        ((ISymbolIdentifier)nodeRef).ShardKey.Should().Be(key);
        ((IStringIdentifier)userId).ShardKey.Should().Be(key);
        ((IStringIdentifier)userId.TypedId).ShardKey.Should().Be(key);
    }

    [Fact]
    public void StableHashShouldMatchTheSharedReferenceVector()
    {
        // act
        var key = ShardKey.New("abc");

        // assert
        key.Value.Should().Be(0x9a994feau);
        key.ToString().Should().Be("9a994fea");
        UserId.Parse("abc").ShardKey.Should().Be(key);
    }

    [Fact]
    public void EveryHexSliceShouldRoundTripAtItsOriginalOffset()
    {
        // arrange
        const string value = "abcdef12";
        var key = ShardKey.Parse(value);

        // act, assert
        for (var start = 0; start < 8; start++)
            for (var length = 1; length <= 8 - start; length++) {
                var slice = key.ToString(start, length);
                slice.Should().Be(value.Substring(start, length));
                var parsed = ShardKey.Parse(slice, start);
                parsed.ToString().Should().Be(slice.PadLeft(start + length, '0').PadRight(8, '0'));
            }
    }
}
