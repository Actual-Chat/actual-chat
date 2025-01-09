using ActualChat.Core.Server.IntegrationTests.Flows;
using ActualChat.Testing.Host;

namespace ActualChat.Core.Server.IntegrationTests;

[CollectionDefinition(nameof(ServerCollection))]
public class ServerCollection : ICollectionFixture<AppHostFixture>;

public class AppHostFixture(IMessageSink messageSink) : ActualChat.Testing.Host.AppHostFixture("server",
    messageSink,
    TestAppHostOptions.Default with {
        ConfigureServices = (ctx, services) => {
            services.AddFlows().Add<SimpleIndexingFlow>().Add<SimpleBatchedIndexingFlow>();
            services.AddSingleton<IndexingFlowTestContext>();
            services.AddSingleton<BatchedIndexingFlowTestContext<SimpleItem, ChatId>>();
        },
    });
