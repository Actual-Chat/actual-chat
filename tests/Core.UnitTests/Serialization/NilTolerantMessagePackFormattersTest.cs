using ActualChat.Serialization.Internal;

namespace ActualChat.Core.UnitTests.Serialization;

public sealed class NilTolerantMessagePackFormattersTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly MessagePackSerializerOptions Options = MessagePackByteSerializer.DefaultOptions;

    [Fact]
    public void ReadsNilAsEmpty()
    {
        // arrange
        var bytes = MessagePackSerializer.Serialize(new NilHolder(null, null), Options);

        // act
        var holder = MessagePackSerializer.Deserialize<Holder>(bytes, Options);

        // assert
        holder.Items.IsEmpty.Should().BeTrue();
        holder.Text.Should().Be("");
    }

    [Theory]
    [InlineData(new int[0], "")]
    [InlineData(new[] { 1, 2, 3 }, "text")]
    public void WritesSameBytesAsDefaultFormatters(int[] items, string text)
    {
        // act
        var bytes = MessagePackSerializer.Serialize(new Holder(new ApiArray<int>(items), text), Options);
        var plainBytes = MessagePackSerializer.Serialize(new PlainHolder(new ApiArray<int>(items), text), Options);

        // assert
        bytes.Should().Equal(plainBytes);
    }

    [Fact]
    public void RoundTrips()
    {
        // arrange
        var holder = new Holder(ApiArray.New(1, 2, 3), "text");

        // act
        var bytes = MessagePackSerializer.Serialize(holder, Options);
        var copy = MessagePackSerializer.Deserialize<Holder>(bytes, Options);

        // assert
        copy.Items.Should().Equal([1, 2, 3]);
        copy.Text.Should().Be("text");
    }

    // Nested types

    [MessagePackObject]
    public sealed record Holder(
        [property: Key(0), MessagePackFormatter(typeof(NilTolerantApiArrayMessagePackFormatter<int>))]
        ApiArray<int> Items,
        [property: Key(1), MessagePackFormatter(typeof(NilTolerantStringMessagePackFormatter))]
        string Text);

    [MessagePackObject]
    public sealed record PlainHolder(
        [property: Key(0)] ApiArray<int> Items,
        [property: Key(1)] string Text);

    [MessagePackObject]
    public sealed record NilHolder(
        [property: Key(0)] int[]? Items,
        [property: Key(1)] string? Text);
}
