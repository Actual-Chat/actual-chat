using ActualChat.Module;
using ActualChat.Testing.Host;

namespace ActualChat.Media.IntegrationTests;

[CollectionDefinition(nameof(MediaCollection))]
public class MediaCollection : ICollectionFixture<AppHostFixture>;

public class AppHostFixture(IMessageSink messageSink)
    : Testing.Host.AppHostFixture("media", messageSink, TestAppHostOptions.Default with {
        ConfigureServices = (_, services) => {
            services.AddSingleton<HttpClientFactoryMock>().AddAlias<IHttpClientFactory, HttpClientFactoryMock>();
            services.AddSingleton<HttpHandlerMock>().AddAlias<IHttpClientFactory, HttpClientFactoryMock>();
        },
        ConfigureHost = (_, cfg) => {
            // CoreServerSettings binds from the "CoreSettings" section (see CoreServerModule.LoadSettings),
            // not the "CoreServerSettings" section AddInMemory<CoreServerSettings> would use.
            cfg.AddInMemoryCollection(
                ($"{nameof(CoreSettings)}:{nameof(CoreServerSettings.EgressHostAllowList)}:0", "domain*.some"));
        }
    });
