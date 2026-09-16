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
        ShardKey.Parse(expected.ToUpper()).Should().Be(key);
        key.GetValue(8).Should().Be(value);
        key.AssertPassesThroughSerializers(Out);
    }

    [Theory]
    [InlineData(1, 0xau, "a")]
    [InlineData(2, 0xabu, "ab")]
    [InlineData(3, 0xabcu, "abc")]
    [InlineData(4, 0xabcdu, "abcd")]
    [InlineData(5, 0xabcdeu, "abcde")]
    [InlineData(6, 0xabcdefu, "abcdef")]
    [InlineData(7, 0xabcdef1u, "abcdef1")]
    [InlineData(8, 0xabcdef12u, "abcdef12")]
    public void DigitCountsShouldSelectLeadingDigits(int digitCount, uint expectedValue, string expectedText)
    {
        // arrange
        var key = new ShardKey(0xabcdef12);

        // act, assert
        key.GetValue(digitCount).Should().Be(expectedValue);
        key.ToString(digitCount).Should().Be(expectedText);
        key.ToString(digitCount).Should().Be(key.ToString()[..digitCount]);
        var parsed = ShardKey.Parse(key.ToString(digitCount));
        parsed.Value.Should().Be(expectedValue << ((8 - digitCount) * 4));
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
            var key = new ShardKey(value << 24);
            var otherKey = new ShardKey((value << 24) | 0x00ffffff);
            key.ToString(1).Should().BeSameAs(otherKey.ToString(1));
            key.ToString(2).Should().BeSameAs(otherKey.ToString(2));
            key.ToString(2).Should().Be(value.ToString("x2"));
        }
    }

    [Theory]
    [InlineData("0", 0u)]
    [InlineData("00", 0u)]
    [InlineData("01", 0x01000000u)]
    [InlineData("12", 0x12000000u)]
    [InlineData("AbC", 0xabc00000u)]
    [InlineData("abcdef1", 0xabcdef10u)]
    public void ShortHexTextShouldParseAsHighOrderDigits(string text, uint expectedValue)
    {
        // act
        var key = ShardKey.Parse(text);
        var isParsed = ShardKey.TryParse(text, out var triedKey);

        // assert
        key.Value.Should().Be(expectedValue);
        isParsed.Should().BeTrue();
        triedKey.Should().Be(key);
        key.ToString(text.Length).Should().Be(text.ToLower());
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
        ShardKey.TryParse(value.AsSpan(), out var spanKey).Should().BeFalse();
        spanKey.Should().Be(default);
        FluentActions.Invoking(() => ShardKey.Parse(value.AsSpan())).Should().Throw<FormatException>();
        FluentActions.Invoking(() => ShardKey.Parse(value)).Should().Throw<FormatException>();
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
    public void SpanSlicesShouldParseAsLeadingDigits()
    {
        // arrange
        const string value = "abcdef12";

        // act, assert
        for (var start = 0; start < 8; start++)
            for (var length = 1; length <= 8 - start; length++) {
                var slice = value.AsSpan(start, length);
                var parsed = ShardKey.Parse(slice);
                parsed.ToString().Should().Be(slice.ToString().PadRight(8, '0'));
                ShardKey.TryParse(slice, out var triedKey).Should().BeTrue();
                triedKey.Should().Be(parsed);
            }
    }
}
