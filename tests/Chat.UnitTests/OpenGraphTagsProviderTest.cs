using ActualChat.Users;

namespace ActualChat.Chat.UnitTests;

public class OpenGraphTagsProviderTest
{
    private static readonly Session Session = Session.New();
    private static readonly UserId BobId = UserId.Parse("bob123");
    private static readonly AliasId BobAliasId = AliasId.Parse("bobby");

    [Theory]
    [InlineData("/u/bob123")]
    [InlineData("/u/bob123?utm_source=share")]
    [InlineData("/u/@bobby")]
    [InlineData("/u/%40bobby")]
    public async Task UserLinkShouldResolveToUserContentInfo(string url)
    {
        // arrange
        var expected = new ContentLinkInfo(BobId.ContentRef, "Bob", null, "Bob's bio");
        var accounts = new Mock<IAccounts>(MockBehavior.Strict);
        accounts.Setup(x => x.Get(Session, BobId, CancellationToken.None)).ReturnsAsync(new Account(BobId));
        var contentLinks = new Mock<IContentLinksBackend>(MockBehavior.Strict);
        contentLinks.Setup(x => x.GetContentInfo(BobId.ContentRef, CancellationToken.None)).ReturnsAsync(expected);
        using var services = CreateServices(accounts.Object, contentLinks.Object);
        var provider = new OpenGraphTagsProvider(services);

        // act
        var info = await provider.GetContentLinkInfo(Session, url, CancellationToken.None);

        // assert
        info.Should().Be(expected);
    }

    [Theory]
    [InlineData("/u/")]
    [InlineData("/u/nobody1")]
    [InlineData("/u/@nobody")]
    [InlineData("/u/~guest1")]
    [InlineData("/settings")]
    public async Task UnresolvedLinkShouldProduceNoContentInfo(string url)
    {
        // arrange
        var accounts = new Mock<IAccounts>(MockBehavior.Strict);
        accounts.Setup(x => x.Get(Session, It.IsAny<UserId>(), CancellationToken.None))
            .ReturnsAsync((Account?)null);
        using var services = CreateServices(accounts.Object, Mock.Of<IContentLinksBackend>(MockBehavior.Strict));
        var provider = new OpenGraphTagsProvider(services);

        // act
        var info = await provider.GetContentLinkInfo(Session, url, CancellationToken.None);

        // assert
        info.Should().BeNull("only a link to an existing non-guest account has user tags");
    }

    // Private methods

    private static ServiceProvider CreateServices(IAccounts accounts, IContentLinksBackend contentLinks)
    {
        var aliases = new Mock<IAliases>(MockBehavior.Strict);
        aliases.Setup(x => x.GetUserIdByAlias(It.IsAny<AliasId>(), CancellationToken.None))
            .ReturnsAsync((AliasId aliasId, CancellationToken _) => aliasId == BobAliasId ? BobId : null);
        return new ServiceCollection()
            .AddSingleton(Mock.Of<IChats>(MockBehavior.Strict))
            .AddSingleton(accounts)
            .AddSingleton(aliases.Object)
            .AddSingleton(contentLinks)
            .BuildServiceProvider();
    }
}
