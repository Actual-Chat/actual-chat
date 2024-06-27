using ActualChat.Chat;
using ActualChat.MLSearch.Documents;
using ActualChat.MLSearch.Indexing;
using ActualChat.MLSearch.Indexing.ChatContent;

namespace ActualChat.MLSearch.UnitTests.Indexing.ChatContent;

public class ChatContentIndexerFactoryTests(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void CreateMethodReturnsIndexerInstance()
    {
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(x => x.GetService(typeof(IChatsBackend)))
            .Returns(Mock.Of<IChatsBackend>());
        serviceProvider
            .Setup(x => x.GetService(typeof(IChatContentDocumentLoader)))
            .Returns(Mock.Of<IChatContentDocumentLoader>());
        serviceProvider
            .Setup(x => x.GetService(typeof(IChatContentMapper)))
            .Returns(Mock.Of<IChatContentMapper>());
        serviceProvider
            .Setup(x => x.GetService(typeof(IChatContentArranger)))
            .Returns(Mock.Of<IChatContentArranger>());
        serviceProvider
            .Setup(x => x.GetService(typeof(ISink<ChatSlice, string>)))
            .Returns(Mock.Of<ISink<ChatSlice, string>>());

        var factory = new ChatContentIndexerFactory(serviceProvider.Object);

        Assert.NotNull(factory.Create());
    }
}
