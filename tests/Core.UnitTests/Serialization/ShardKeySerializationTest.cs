using ActualChat.Serialization.Internal;
using MessagePack.ImmutableCollection;
using MessagePack.Resolvers;

namespace ActualChat.Core.UnitTests.Serialization;

public sealed class ShardKeySerializationTest(ITestOutputHelper @out) : TestBase(@out)
{
    // Mirrors AppMessagePackResolverSettings.StandardResolvers on a platform where
    // RuntimeFeature.IsDynamicCodeSupported is false (iOS, Native AOT)
    private static readonly IFormatterResolver NoDynamicResolver = CompositeResolver.Create(
        BuiltinResolver.Instance,
        AttributeFormatterResolver.Instance,
        SourceGeneratedFormatterResolver.Instance,
        ImmutableCollectionResolver.Instance,
        DynamicGenericResolver.Instance);
    private static readonly MessagePackSerializerOptions NoDynamicOptions =
        MessagePackSerializerOptions.Standard.WithResolver(NoDynamicResolver);

    [Fact]
    public void ResolvesWithoutDynamicCode()
    {
        // act
        var formatter = NoDynamicResolver.GetFormatter<ShardKey>();

        // assert
        formatter.Should().BeOfType<ShardKeyMessagePackFormatter>();
    }

    [Fact]
    public void RoundTripsWithoutDynamicCode()
    {
        // act
        var bytes = MessagePackSerializer.Serialize(ShardKey.New(-1234567), NoDynamicOptions);
        var shardKey = MessagePackSerializer.Deserialize<ShardKey>(bytes, NoDynamicOptions);
        var headBytes = MessagePackSerializer.Serialize(ShardKey.New(-1234567).Head(2), NoDynamicOptions);
        var headShardKey = MessagePackSerializer.Deserialize<ShardKey>(headBytes, NoDynamicOptions);

        // assert
        shardKey.Should().Be(ShardKey.New(-1234567));
        headShardKey.Should().Be(ShardKey.New(-1234567).Head(2));
        headShardKey.Size.Should().Be(2);
    }

    [Fact]
    public void WritesValueSizePair()
    {
        // act
        var options = MessagePackByteSerializer.DefaultOptions;
        var bytes = MessagePackSerializer.Serialize(ShardKey.New(42), options);

        // assert - Size is part of the key identity, so a bare integer would restore
        // every key as a 0-digit one
        MessagePackSerializer.ConvertToJson(bytes, options).Should().Be("[42,8]");
    }

    [Fact]
    public void KeylessWritesTheSamePair()
    {
        // act
        var options = MessagePackSerializerOptions.Standard.WithResolver(AppMessagePackKeylessResolver.Instance);
        var bytes = MessagePackSerializer.Serialize(ShardKey.New(42), options);

        // assert - AttributeFormatterResolver wins in the keyless chain as well
        MessagePackSerializer.ConvertToJson(bytes, options).Should().Be("[42,8]");
    }

    [Fact]
    public void PassesThroughAllSerializers()
    {
        // act
        var act = () => ShardKey.New(7).AssertPassesThroughAllSerializers();

        // assert
        act.Should().NotThrow();
    }
}
