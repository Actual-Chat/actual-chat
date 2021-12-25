namespace ActualChat.Core.UnitTests;

public class IdentifierTest : TestBase
{
    public IdentifierTest(ITestOutputHelper @out) : base(@out) { }

    [Fact]
    public void NoneTest()
    {
        default(TestStringId).Should().Be(new TestStringId(""));
        default(TestStringId).Should().Be(TestStringId.None);
        TestStringId.None.Value.Should().Be("");
    }

    [Fact]
    public void EqualityTest()
    {
        new TestStringId("1").Should().Be(new TestStringId("1"));
        new TestStringId("1").Should().NotBe(new TestStringId("2"));
        new TestStringId("1").Should().NotBe(TestStringId.None);
    }

    [Fact]
    public void SerializationTest()
    {
        default(TestStringId).AssertPassesThroughAllSerializers(Out);

        var chatId = new TestStringId("1");
        chatId.AssertPassesThroughAllSerializers(Out);

        var s1 = new NewtonsoftJsonSerializer();
        s1.Write(chatId).Should().Be("\"1\"");
        var s2 = new SystemJsonSerializer();
        s2.Write(chatId).Should().Be("\"1\"");
    }
}
