using ActualChat.Serialization.Internal;

namespace ActualChat.Core.UnitTests.Serialization;

public sealed class Size2DSerializationTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly MessagePackSerializerOptions KeylessOptions =
        new(AppMessagePackKeylessResolver.Instance);
    private static readonly MessagePackByteSerializer Default = MessagePackByteSerializer.Default;
    private static readonly MessagePackByteSerializer Keyless = new(KeylessOptions);

    [Fact]
    public void DefaultWritesKeyArray()
    {
        // act
        var bytes = Default.Write(new Size2D(640, 480), typeof(Size2D)).WrittenSpan.ToArray();
        var json = MessagePackSerializer.ConvertToJson(bytes, MessagePackByteSerializer.DefaultOptions);

        // assert
        json.Should().Be("[640,480]");
    }

    [Fact]
    public void KeylessWritesPropertyNameMap()
    {
        // act
        var bytes = Keyless.Write(new Size2D(640, 480), typeof(Size2D)).WrittenSpan.ToArray();
        var json = MessagePackSerializer.ConvertToJson(bytes, KeylessOptions);

        // assert - the TS clients (msgpack6ck) read VideoFormat.Size by name
        json.Should().Be("{\"Width\":640,\"Height\":480}");
    }

    [Fact]
    public void PassesThroughAllSerializers()
    {
        // act
        var act = () => new Size2D(1920, 1080).AssertPassesThroughAllSerializers();

        // assert
        act.Should().NotThrow();
    }

    [Fact]
    public void PassesThroughAllSerializersInsideMetadataBag()
    {
        // arrange
        var bag = MetadataBag.Empty.Set("Size", new Size2D(1280, 720));

        // act
        var result = bag.PassThroughAllSerializers();

        // assert
        result["Size"].Should().Be(new Size2D(1280, 720));
    }
}
