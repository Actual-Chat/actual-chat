using ActualChat.AI;
using ActualChat.Chat.ML;
using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[CollectionDefinition(nameof(ImageSuggestionsCollection))]
public class ImageSuggestionsCollection : ICollectionFixture<ImageSuggestionsCollection.AppHostFixture>
{
    public class AppHostFixture(IMessageSink messageSink)
        : ActualChat.Testing.Host.AppHostFixture("image-suggestions", messageSink, TestAppHostOptions.Default with {
            ConfigureServices = (_, services) => {
                services.AddSingleton<ImageGeneratorMock>().AddAlias<IImageGenerator, ImageGeneratorMock>();
                services.AddSingleton<ChatImageDescriberMock>()
                    .AddAlias<IChatImageDescriber, ChatImageDescriberMock>();
            },
        });
}
