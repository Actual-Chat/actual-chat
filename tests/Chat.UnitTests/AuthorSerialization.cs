namespace ActualChat.Chat.UnitTests;

public class AuthorSerializationTest
{
    [Fact]
    public void BasicTest()
    {
        var authorId = AuthorId.New(GroupChatId.Parse("testChatId"), 0);
        var author = new AuthorFull(null!, authorId) {
            Avatar = new (Symbol.Empty) {
                Name = "Alex",
            },
        };
        var sa = author.PassThroughSystemJsonSerializer();
        sa.Id.Should().Be(author.Id);
        sa.Version.Should().Be(author.Version);
        sa.Avatar.Id.Should().Be(author.Avatar.Id);
        sa.Avatar.Name.Should().Be(author.Avatar.Name);
    }
}
