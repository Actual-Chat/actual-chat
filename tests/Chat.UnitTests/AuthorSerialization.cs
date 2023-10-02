namespace ActualChat.Chat.UnitTests;

public class AuthorSerializationTest
{
    [Fact]
    public void BasicTest()
    {
        var a = new AuthorFull(new AuthorId(new ChatId("testChatId"), 0, AssumeValid.Option)) {
            Avatar = new (Symbol.Empty) {
                Name = "Alex",
            },
        };
        var sa = a.PassThroughSystemJsonSerializer();
        sa.Id.Should().Be(a.Id);
        sa.Version.Should().Be(a.Version);
        sa.Avatar.Id.Should().Be(a.Avatar.Id);
        sa.Avatar.Name.Should().Be(a.Avatar.Name);
    }
}
