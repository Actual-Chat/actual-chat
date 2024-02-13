using ActualChat.Testing.Host;

namespace ActualChat.Search.IntegrationTests;

[CollectionDefinition(nameof(SearchCollection))]
public class SearchCollection : ICollectionFixture<AppHostFixture>;

public class AppHostFixture(IMessageSink messageSink)
    : ActualChat.Testing.Host.AppHostFixture("search", messageSink, TestAppHostOptions.Default with {
        AppConfigurationExtender = cfg => {
            cfg.AddInMemory(("SearchSettings:IsSearchEnabled", "true"));
        },
    });
