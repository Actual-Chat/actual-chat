using ActualChat.Search.Module;
using ActualChat.Testing.Host;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

[CollectionDefinition(nameof(SearchUICollection))]
public class SearchUICollection : ICollectionFixture<SearchAppHostFixture>;

public class SearchAppHostFixture(IMessageSink messageSink) : ActualChat.Testing.Host.AppHostFixture("search-ui",
    messageSink,
    TestAppHostOptions.Default with {
        ConfigureHost = (_, cfg) => {
            cfg.AddInMemoryCollection(($"{nameof(SearchSettings)}:{nameof(SearchSettings.IsSearchEnabled)}", "true"));
            cfg.AddInMemoryCollection(($"{nameof(SearchSettings)}:{nameof(SearchSettings.ContactIndexingDelay)}", "00:00:01"));
            cfg.AddInMemoryCollection(($"{nameof(SearchSettings)}:{nameof(SearchSettings.ContactIndexingSignalInterval)}", "00:00:00.5"));
        },
    });
