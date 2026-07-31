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

        // assert
        shardKey.Should().Be(ShardKey.New(-1234567));
    }

    [Fact]
    public void WritesBareInt()
    {
        // act
        var options = MessagePackByteSerializer.DefaultOptions;
        var bytes = MessagePackSerializer.Serialize(ShardKey.New(42), options);

        // assert - a bare integer, i.e. exactly what an int writes; the pre-formatter
        // DynamicObjectResolver layout was the [Key(0)] array-of-1 0x91 0x2A
        bytes.Should().Equal(MessagePackSerializer.Serialize(42, options));
        MessagePackSerializer.ConvertToJson(bytes, options).Should().Be("42");
    }

    [Fact]
    public void KeylessWritesBareIntToo()
    {
        // act
        var options = MessagePackSerializerOptions.Standard.WithResolver(AppMessagePackKeylessResolver.Instance);
        var bytes = MessagePackSerializer.Serialize(ShardKey.New(42), options);

        // assert - AttributeFormatterResolver wins in the keyless chain as well,
        // so there's no "{"Value":42}" map form anymore
        MessagePackSerializer.ConvertToJson(bytes, options).Should().Be("42");
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
