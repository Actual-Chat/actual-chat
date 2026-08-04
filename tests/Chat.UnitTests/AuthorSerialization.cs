namespace ActualChat.Chat.UnitTests;

public class AuthorSerializationTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void BasicTest()
    {
        var authorId = AuthorId.New(GroupChatId.Parse("testChatId"), 0);
        var userId = UserId.New();
        var author = new AuthorFull(userId, authorId) {
            Avatar = new (Symbol.Empty) {
                Name = "Alex",
            },
        };
        var sa = author.PassThroughSerializers(Out);
        sa.Id.Should().Be(author.Id);
        sa.Version.Should().Be(author.Version);
        sa.UserId.Should().Be(userId);
        sa.Avatar.Id.Should().Be(author.Avatar.Id);
        sa.Avatar.Name.Should().Be(author.Avatar.Name);
    }
}
