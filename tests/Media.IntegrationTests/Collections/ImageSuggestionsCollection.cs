using ActualChat.AI;
using ActualChat.Testing.Host;

namespace ActualChat.Media.IntegrationTests;

[CollectionDefinition(nameof(ImageSuggestionsCollection))]
public class ImageSuggestionsCollection : ICollectionFixture<ImageSuggestionsAppHostFixture>;

public class ImageSuggestionsAppHostFixture(IMessageSink messageSink)
    : Testing.Host.AppHostFixture("image-suggestions", messageSink, TestAppHostOptions.Default with {
        ConfigureServices = (_, services) => {
            services.AddSingleton<ImageGeneratorMock>().AddAlias<IImageGenerator, ImageGeneratorMock>();
        },
    });
