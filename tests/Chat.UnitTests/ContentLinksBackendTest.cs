using ActualChat.DependencyInjection;
using ActualChat.Users;

namespace ActualChat.Chat.UnitTests;

public class ContentLinksBackendTest
{
    [Theory]
    [InlineData("u:abcdef")]
    [InlineData("c:abcdef")]
    [InlineData("ce:abcdef:0:1")]
    [InlineData("a:abcdef:1")]
    [InlineData("p:abcdefghij")]
    public async Task MissingObjectsShouldProduceRemovedMetadata(string value)
    {
        // arrange
        var id = TypedObjectId.Parse(value);
        var accounts = new Mock<IAccountsBackend>(MockBehavior.Strict);
        var chats = new Mock<IChatsBackend>(MockBehavior.Strict);
        var authors = new Mock<IAuthorsBackend>(MockBehavior.Strict);
        var places = new Mock<IPlacesBackend>(MockBehavior.Strict);
        switch (id.ObjectId) {
            case UserId userId:
                accounts.Setup(x => x.Get(userId, CancellationToken.None)).ReturnsAsync((AccountFull?)null);
                break;
            case ChatId chatId:
                chats.Setup(x => x.Get(chatId, CancellationToken.None)).ReturnsAsync((Chat?)null);
                break;
            case ChatEntryId entryId:
                var range = Constants.Chat.EntryIdTiles.GetTile(entryId.LocalId).Range;
                chats.Setup(x => x.GetTile(entryId.ChatId, range, false, CancellationToken.None))
                    .ReturnsAsync(new ChatTile(range, false, []));
                break;
            case AuthorId authorId:
                authors.Setup(x => x.Get(authorId.ChatId, authorId, RequestedAuthorKind.Full, CancellationToken.None))
                    .ReturnsAsync((AuthorFull?)null);
                break;
            case PlaceId placeId:
                places.Setup(x => x.Get(placeId, CancellationToken.None)).ReturnsAsync((Place?)null);
                break;
        }
        using var services = CreateServices(accounts.Object, chats.Object, authors.Object, places.Object);
        var backend = new ContentLinksBackend(services);

        // act
        var info = await backend.GetContentInfo(id, CancellationToken.None);

        // assert
        info.Should().Be(ContentLinkInfo.RemovedOrUnknown(id));
        accounts.VerifyAll();
        chats.VerifyAll();
        authors.VerifyAll();
        places.VerifyAll();
    }

    [Fact]
    public async Task UnsupportedObjectTypesShouldBeRejected()
    {
        // arrange
        using var services = CreateServices(
            Mock.Of<IAccountsBackend>(MockBehavior.Strict),
            Mock.Of<IChatsBackend>(MockBehavior.Strict),
            Mock.Of<IAuthorsBackend>(MockBehavior.Strict),
            Mock.Of<IPlacesBackend>(MockBehavior.Strict));
        var backend = new ContentLinksBackend(services);
        var id = Language.Parse("en").TypedId;

        // act
        var action = () => backend.GetContentInfo(id, CancellationToken.None);

        // assert
        await action.Should().ThrowAsync<NotSupportedException>();
    }

    // Private methods

    private static ServiceProvider CreateServices(
        IAccountsBackend accounts,
        IChatsBackend chats,
        IAuthorsBackend authors,
        IPlacesBackend places)
        => new ServiceCollection()
            .AddSingleton(accounts)
            .AddSingleton(chats)
            .AddSingleton(authors)
            .AddSingleton(places)
            .AddSingleton(services => new KeyedFactory<IBackendChatMarkupHub, ChatId>(services))
            .BuildServiceProvider();
}
