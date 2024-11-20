using ActualChat.MLSearch.Module;
using ActualChat.Testing.Host;

namespace ActualChat.UI.Blazor.App.IntegrationTests;

[CollectionDefinition(nameof(SearchUICollection))]
public class SearchUICollection : ICollectionFixture<SearchAppHostFixture>;

public class SearchAppHostFixture(IMessageSink messageSink) : ActualChat.Testing.Host.AppHostFixture("search-ui",
    messageSink,
    TestAppHostOptions.Default with {
        ConfigureHost = (_, cfg) => {
            cfg.AddInMemoryCollection(($"{nameof(MLSearchSettings)}:{nameof(MLSearchSettings.IsEnabled)}", "true"));
            cfg.AddInMemoryCollection(($"{nameof(MLSearchSettings)}:{nameof(MLSearchSettings.IsInitialIndexingDisabled)}", "true"));
            cfg.AddInMemoryCollection(($"{nameof(MLSearchSettings)}:{nameof(MLSearchSettings.IndexingDelay)}", "00:00:03"));
            cfg.AddInMemoryCollection(($"{nameof(MLSearchSettings)}:{nameof(MLSearchSettings.IndexingRecheckInterval)}", "00:00:03.5"));
            cfg.AddInMemoryCollection(($"{nameof(MLSearchSettings)}:{nameof(MLSearchSettings.ContactIndexingSignalInterval)}", "00:00:00.5"));
        },
    });
