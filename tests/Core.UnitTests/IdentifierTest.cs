using Stl.Serialization;
using Stl.Testing;
using Xunit.Abstractions;

namespace ActualChat.Core.UnitTests
{
    public class IdentifierTest : TestBase
    {
        public IdentifierTest(ITestOutputHelper @out) : base(@out) { }

        [Fact]
        public void NoneTest()
        {
            default(ChatId).Should().Be(new ChatId(""));
            default(ChatId).Should().Be(ChatId.None);
            ChatId.None.Value.Should().Be("");
        }

        [Fact]
        public void EqualityTest()
        {
            new ChatId("1").Should().Be(new ChatId("1"));
            new ChatId("1").Should().NotBe(new ChatId("2"));
            new ChatId("1").Should().NotBe(ChatId.None);
        }

        [Fact]
        public void SerializationTest()
        {
            default(ChatId).AssertPassesThroughAllSerializers(Out);

            var chatId = new ChatId("1");
            chatId.AssertPassesThroughAllSerializers(Out);

            var s1 = new NewtonsoftJsonSerializer();
            s1.Write(chatId).Should().Be("\"1\"");
            var s2 = new SystemJsonSerializer();
            s2.Write(chatId).Should().Be("\"1\"");
        }
    }
}
