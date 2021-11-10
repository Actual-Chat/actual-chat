namespace ActualChat.Chat.UnitTests;

public class ChatAuthorSerialization
{
    [Fact]
    public void BasicTest()
    {
        SerializationTestExt.SystemJsonOptions = SystemJsonSerializer.DefaultOptions;
        var ca = new ChatAuthor() {
            Name = "Alex",
        };
        ca.PassThroughSystemJsonSerializer().Should().Be(ca);
    }

}
