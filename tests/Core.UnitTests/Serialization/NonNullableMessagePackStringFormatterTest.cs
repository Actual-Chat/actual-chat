using ActualChat.Serialization.Internal;

namespace ActualChat.Core.UnitTests.Serialization;

public sealed class NonNullableMessagePackStringFormatterTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly MessagePackSerializerOptions Options = MessagePackByteSerializer.DefaultOptions;

    [Fact]
    public void ReadsNilAsEmpty()
    {
        // arrange
        var bytes = MessagePackSerializer.Serialize(new NilHolder(null), Options);

        // act
        var holder = MessagePackSerializer.Deserialize<Holder>(bytes, Options);

        // assert
        holder.Text.Should().Be("");
    }

    [Theory]
    [InlineData("")]
    [InlineData("text")]
    public void WritesSameBytesAsDefaultFormatter(string text)
    {
        // act
        var bytes = MessagePackSerializer.Serialize(new Holder(text), Options);
        var plainBytes = MessagePackSerializer.Serialize(new PlainHolder(text), Options);

        // assert
        bytes.Should().Equal(plainBytes);
    }

    // Nested types

    [MessagePackObject]
    public sealed record Holder(
        [property: Key(0), MessagePackFormatter(typeof(NonNullableMessagePackStringFormatter))]
        string Text);

    [MessagePackObject]
    public sealed record PlainHolder([property: Key(0)] string Text);

    [MessagePackObject]
    public sealed record NilHolder([property: Key(0)] string? Text);
}
