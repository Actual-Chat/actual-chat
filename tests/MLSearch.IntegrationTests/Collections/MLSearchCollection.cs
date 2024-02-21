using ActualChat.Testing.Host;

namespace ActualChat.MLSearch.IntegrationTests;

[CollectionDefinition(nameof(MLSearchCollection))]
public class MLSearchCollection : ICollectionFixture<AppHostFixture>;

public class AppHostFixture(IMessageSink messageSink)
    : Testing.Host.AppHostFixture("mlsearch", messageSink, TestAppHostOptions.Default with {
        // AppConfigurationExtender = cfg => {
        //     cfg.AddInMemory(("SearchSettings:IsSearchEnabled", "true"));
        // },
    });
