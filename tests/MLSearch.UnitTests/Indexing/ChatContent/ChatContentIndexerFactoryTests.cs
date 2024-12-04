using ActualChat.Chat;
using ActualChat.MLSearch.Documents;
using ActualChat.MLSearch.Indexing;
using ActualChat.MLSearch.Indexing.ChatContent;

namespace ActualChat.MLSearch.UnitTests.Indexing.ChatContent;

public class ChatContentIndexerFactoryTests(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public async Task CreateMethodReturnsIndexerInstance()
    {
        var serviceProvider = new Mock<IServiceProvider>(MockBehavior.Loose);
        serviceProvider
            .Setup(x => x.GetService(typeof(IChatContentArrangerSelector)))
            .Returns(Mock.Of<IChatContentArrangerSelector>(MockBehavior.Loose));
        var chatsBackend = Mock.Of<IChatsBackend>(MockBehavior.Loose);
        serviceProvider
            .Setup(x => x.GetService(typeof(IChatsBackend)))
            .Returns(chatsBackend);
        serviceProvider
            .Setup(x => x.GetService(typeof(IChatContentDocumentLoader)))
            .Returns(Mock.Of<IChatContentDocumentLoader>(MockBehavior.Loose));
        serviceProvider
            .Setup(x => x.GetService(typeof(IChatContentMapper)))
            .Returns(Mock.Of<IChatContentMapper>(MockBehavior.Loose));
        serviceProvider
            .Setup(x => x.GetService(typeof(ISink<ChatSlice, string>)))
            .Returns(Mock.Of<ISink<ChatSlice, string>>(MockBehavior.Loose));

        var factory = new ChatContentIndexerFactory(serviceProvider.Object);

        var chatId = new ChatId(Generate.Option);
        var indexer = await factory.Create(chatId);
        Assert.NotNull(indexer);
        Assert.Equal(chatId, indexer.ChatId);
    }
}
