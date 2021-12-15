namespace ActualChat.Core.UnitTests;

public class IdentifierTest : TestBase
{
    public IdentifierTest(ITestOutputHelper @out) : base(@out) { }

    [Fact]
    public void NoneTest()
    {
        default(IdentifierTestingId).Should().Be(new IdentifierTestingId(""));
        default(IdentifierTestingId).Should().Be(IdentifierTestingId.None);
        IdentifierTestingId.None.Value.Should().Be("");
    }

    [Fact]
    public void EqualityTest()
    {
        new IdentifierTestingId("1").Should().Be(new IdentifierTestingId("1"));
        new IdentifierTestingId("1").Should().NotBe(new IdentifierTestingId("2"));
        new IdentifierTestingId("1").Should().NotBe(IdentifierTestingId.None);
    }

    [Fact]
    public void SerializationTest()
    {
        default(IdentifierTestingId).AssertPassesThroughAllSerializers(Out);

        var chatId = new IdentifierTestingId("1");
        chatId.AssertPassesThroughAllSerializers(Out);

        var s1 = new NewtonsoftJsonSerializer();
        s1.Write(chatId).Should().Be("\"1\"");
        var s2 = new SystemJsonSerializer();
        s2.Write(chatId).Should().Be("\"1\"");
    }
}
