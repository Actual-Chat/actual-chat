using ActualChat.Testing.Host;

namespace ActualChat.Media.IntegrationTests;

[CollectionDefinition(nameof(MediaCollection))]
public class MediaCollection : ICollectionFixture<AppHostFixture>;

public class AppHostFixture(IMessageSink messageSink)
    : ActualChat.Testing.Host.AppHostFixture("media", messageSink, TestAppHostOptions.Default with {
        ConfigureServices = (_, services) => {
            services.AddSingleton<HttpClientFactoryMock>().AddAlias<IHttpClientFactory, HttpClientFactoryMock>();
            services.AddSingleton<HttpHandlerMock>().AddAlias<IHttpClientFactory, HttpClientFactoryMock>();
        },
    });
