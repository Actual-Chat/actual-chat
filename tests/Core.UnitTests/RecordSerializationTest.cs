namespace ActualChat.Core.UnitTests;

public class RecordSerializationTest : TestBase
{
    [DataContract]
    public sealed record Vec2(
        [property: DataMember] int X,
        [property: DataMember] int Y);

    public RecordSerializationTest(ITestOutputHelper @out) : base(@out) { }

    [Fact]
    public void SerializeVec2()
    {
        var v = new Vec2(1, 2);
        v.AssertPassesThroughAllSerializers(Out);
    }
}
