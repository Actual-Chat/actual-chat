using ActualChat.Logging;
using ActualChat.Testing.Host;

namespace ActualChat.UI.Blazor.IntegrationTests;

[CollectionDefinition(nameof(UICollection))]
public class UICollection : ICollectionFixture<AppHostFixture>;

public class AppHostFixture(IMessageSink messageSink) : ActualChat.Testing.Host.AppHostFixture("ui",
    messageSink,
    TestAppHostOptions.Default with {
        ConfigureServices = (ctx, services) => {
            services.AddLogging(logging => logging.AddTailLogger());
        },
    });
