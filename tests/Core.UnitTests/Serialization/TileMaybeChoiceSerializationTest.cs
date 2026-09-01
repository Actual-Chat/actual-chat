using ActualChat.Serialization.Internal;
using MessagePack.Resolvers;

namespace ActualChat.Core.UnitTests.Serialization;

// AotResolver reproduces AppMessagePackResolverSettings.StandardResolvers as it looks on a
// platform where RuntimeFeature.IsDynamicCodeSupported is false (iOS, Native AOT). Core also
// disables the MessagePack source generator, so [MessagePackFormatter] is the only thing left
// to resolve its [MessagePackObject] generics there - without it these types silently work on
// desktop and throw on the client.
public sealed class TileMaybeChoiceSerializationTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly IFormatterResolver AotResolver = CompositeResolver.Create(
        AppMessagePackResolverSettings.StandardResolvers
            .Where(r => r is not (DynamicObjectResolver or DynamicUnionResolver))
            .ToArray());
    private static readonly MessagePackSerializerOptions AotOptions =
        MessagePackSerializerOptions.Standard.WithResolver(AotResolver);
    private static readonly TileLayer<long> Tiles16 = new(0L, 16L);
    private static readonly TileLayer<long> Tiles16K = new(0L, 16_384L);

    [Fact]
    public void TileFormatterResolves()
        => AotResolver.GetFormatter<Tile<long>>().Should().BeOfType<TileMessagePackFormatter<long>>();

    [Fact]
    public void MaybeFormatterResolves()
    {
        AotResolver.GetFormatter<Maybe<string>>().Should().BeOfType<MaybeMessagePackFormatter<string>>();
        AotResolver.GetFormatter<Maybe<int>>().Should().BeOfType<MaybeMessagePackFormatter<int>>();
    }

    [Fact]
    public void ChoiceFormatterResolves()
        => AotResolver.GetFormatter<Choice<int, string>>().Should().BeOfType<ChoiceMessagePackFormatter<int, string>>();

    [Fact]
    public void TileRoundTrips()
    {
        var tile = Tiles16.GetTile(20L);
        var json = ToJson(tile);
        Out.WriteLine(json);
        json.Should().Be("[[16,32]]");

        var actual = RoundTrip(tile);
        actual.Range.Should().Be(tile.Range);
        actual.Layer.Should().BeNull();
    }

    [Fact]
    public void TileWithNestedRangeRoundTrips()
    {
        var tile = Tiles16K.GetTile(1_000_000L);
        RoundTrip(tile).Range.Should().Be(tile.Range);
    }

    [Fact]
    public void MaybeValueRoundTrips()
    {
        var maybe = Maybe.Value("hello");
        ToJson(maybe).Should().Be("[true,\"hello\"]");
        RoundTrip(maybe).Should().Be(maybe);

        var maybeInt = Maybe.Value(42);
        ToJson(maybeInt).Should().Be("[true,42]");
        RoundTrip(maybeInt).Should().Be(maybeInt);
    }

    [Fact]
    public void MaybeNoneRoundTrips()
    {
        var maybe = Maybe.None<string>();
        ToJson(maybe).Should().Be("[false,null]");

        var actual = RoundTrip(maybe);
        actual.Should().Be(maybe);
        actual.HasValue.Should().BeFalse();

        RoundTrip(Maybe.None<int>()).Should().Be(Maybe.None<int>());
    }

    [Fact]
    public void MaybeNullRoundTrips()
        => RoundTrip((Maybe<string>?)null).Should().BeNull();

    [Fact]
    public void ChoiceValueRoundTrips()
    {
        var choice = Choice.Value<int, string>(42);
        ToJson(choice).Should().Be("[true,42,null]");

        var actual = RoundTrip(choice);
        actual.Should().Be(choice);
        actual.Value.Should().Be(42);
    }

    [Fact]
    public void ChoiceAlternativeRoundTrips()
    {
        var choice = Choice.Alternative<int, string>("oops");
        ToJson(choice).Should().Be("[false,null,\"oops\"]");

        var actual = RoundTrip(choice);
        actual.Should().Be(choice);
        actual.Alternative.Should().Be("oops");
    }

    [Fact]
    public void ChoiceNullRoundTrips()
        => RoundTrip((Choice<int, string>?)null).Should().BeNull();

    [Fact]
    public void ChoicePrefixMatchesMaybe()
    {
        // The Choice layout is the Maybe layout plus the Alternative slot.
        var choiceJson = ToJson(Choice.Value<int, string>(42));
        var maybeJson = ToJson(Maybe.Value(42));
        choiceJson.Should().StartWith(maybeJson[..^1]);
    }

    [Fact]
    public void DefaultOptionsRoundTripToo()
    {
        // The same formatters must be picked by the app's regular (dynamic-capable) chain.
        var options = MessagePackByteSerializer.DefaultOptions;
        var tile = Tiles16.GetTile(20L);
        var bytes = MessagePackSerializer.Serialize(tile, options);
        MessagePackSerializer.Deserialize<Tile<long>>(bytes, options).Range.Should().Be(tile.Range);

        var choice = Choice.Alternative<int, string>("oops");
        var choiceBytes = MessagePackSerializer.Serialize(choice, options);
        MessagePackSerializer.Deserialize<Choice<int, string>>(choiceBytes, options).Should().Be(choice);
    }

    // Private methods

    private static T RoundTrip<T>(T value)
    {
        var bytes = MessagePackSerializer.Serialize(value, AotOptions);
        return MessagePackSerializer.Deserialize<T>(bytes, AotOptions);
    }

    private static string ToJson<T>(T value)
        => MessagePackSerializer.ConvertToJson(MessagePackSerializer.Serialize(value, AotOptions), AotOptions);
}
