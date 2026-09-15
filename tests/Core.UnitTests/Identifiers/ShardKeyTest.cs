namespace ActualChat.Core.UnitTests.Identifiers;

public sealed class ShardKeyTest(ITestOutputHelper @out) : TestBase(@out)
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
        ShardKey.Parse(expected.ToUpperInvariant()).Should().Be(key);
        key.GetValue(8).Should().Be(value);
        key.AssertPassesThroughSerializers(Out);
    }

    [Theory]
    [InlineData(1, 0x2u, "2")]
    [InlineData(2, 0x12u, "12")]
    [InlineData(3, 0xf12u, "f12")]
    [InlineData(4, 0xef12u, "ef12")]
    [InlineData(5, 0xdef12u, "def12")]
    [InlineData(6, 0xcdef12u, "cdef12")]
    [InlineData(7, 0xbcdef12u, "bcdef12")]
    [InlineData(8, 0xabcdef12u, "abcdef12")]
    public void DigitCountsShouldSelectTrailingDigits(int digitCount, uint expectedValue, string expectedText)
    {
        // arrange
        var key = new ShardKey(0xabcdef12);

        // act, assert
        key.GetValue(digitCount).Should().Be(expectedValue);
        key.ToString(digitCount).Should().Be(expectedText);
        var parsed = ShardKey.Parse(key.ToString(digitCount));
        parsed.Value.Should().Be(expectedValue);
        parsed.ToString(digitCount).Should().Be(expectedText);
        ShardKey.TryParse(expectedText, out var triedKey).Should().BeTrue();
        triedKey.Should().Be(parsed);
    }

    [Theory]
    [InlineData(int.MinValue, 0u, "")]
    [InlineData(-1, 0u, "")]
    [InlineData(0, 0u, "")]
    [InlineData(8, 0xabcdef12u, "abcdef12")]
    [InlineData(9, 0xabcdef12u, "abcdef12")]
    [InlineData(int.MaxValue, 0xabcdef12u, "abcdef12")]
    public void DigitCountsShouldBeClamped(int digitCount, uint expectedValue, string expectedText)
    {
        // arrange
        var key = new ShardKey(0xabcdef12);

        // act, assert
        key.GetValue(digitCount).Should().Be(expectedValue);
        key.ToString(digitCount).Should().Be(expectedText);
    }

    [Fact]
    public void ShortHexStringsShouldBeReused()
    {
        // act, assert
        for (var value = 0u; value < 256; value++) {
            var key = new ShardKey(value);
            var otherKey = new ShardKey(value | 0xffffff00);
            key.ToString(1).Should().BeSameAs(otherKey.ToString(1));
            key.ToString(2).Should().BeSameAs(otherKey.ToString(2));
            key.ToString(2).Should().Be(value.ToString("x2"));
        }
    }

    [Theory]
    [InlineData("0", 0u)]
    [InlineData("00", 0u)]
    [InlineData("01", 1u)]
    [InlineData("12", 0x12u)]
    [InlineData("AbC", 0xabcu)]
    [InlineData("abcdef1", 0xabcdef1u)]
    public void ShortHexTextShouldParseAsLowOrderDigits(string text, uint expectedValue)
    {
        // act
        var key = ShardKey.Parse(text);
        var isParsed = ShardKey.TryParse(text, out var triedKey);

        // assert
        key.Value.Should().Be(expectedValue);
        isParsed.Should().BeTrue();
        triedKey.Should().Be(key);
        key.ToString(text.Length).Should().Be(text.ToLowerInvariant());
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
        var isParsed = ShardKey.TryParse(value, out var key);

        // assert
        isParsed.Should().BeFalse();
        key.Should().Be(default);
        FluentActions.Invoking(() => ShardKey.Parse(value)).Should().Throw<FormatException>();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(7)]
    [InlineData(8)]
    public void InvalidParseOffsetsShouldBeRejected(int startIndex)
    {
        // act
        var isParsed = ShardKey.TryParse("ab", startIndex, out var key);

        // assert
        isParsed.Should().BeFalse();
        key.Should().Be(default);
        FluentActions.Invoking(() => ShardKey.Parse("ab", startIndex)).Should().Throw<FormatException>();
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
        userId.ContentRef.ShardKey.Should().Be(key);
        nodeRef.ShardKey.Should().Be(key);
        ((ISymbolIdentifier)nodeRef).ShardKey.Should().Be(key);
        ((IStringIdentifier)userId).ShardKey.Should().Be(key);
        ((IStringIdentifier)userId.ContentRef).ShardKey.Should().Be(key);
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
    public void HexTextShouldParseAtItsOriginalOffset()
    {
        // arrange
        const string value = "abcdef12";

        // act, assert
        for (var start = 0; start < 8; start++)
            for (var length = 1; length <= 8 - start; length++) {
                var slice = value.Substring(start, length);
                var parsed = ShardKey.Parse(slice, start);
                parsed.ToString().Should().Be(slice.PadLeft(start + length, '0').PadRight(8, '0'));
            }
    }
}
