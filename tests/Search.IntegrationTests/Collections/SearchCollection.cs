using ActualChat.Search.Module;
using ActualChat.Testing.Host;

namespace ActualChat.Search.IntegrationTests;

[CollectionDefinition(nameof(SearchCollection))]
public class SearchCollection : ICollectionFixture<AppHostFixture>;

public class AppHostFixture(IMessageSink messageSink)
    : ActualChat.Testing.Host.AppHostFixture("search", messageSink, TestAppHostOptions.Default with {
        ConfigureHost = (_, cfg) => {
            cfg.AddInMemory(($"{nameof(SearchSettings)}:{nameof(SearchSettings.IsSearchEnabled)}", "true"));
        },
    });
