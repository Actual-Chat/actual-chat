using ActualChat.MLSearch.Module;
using ActualChat.Testing.Host;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

[CollectionDefinition(nameof(SearchUICollection))]
public class SearchUICollection : ICollectionFixture<SearchAppHostFixture>;

public class SearchAppHostFixture(IMessageSink messageSink) : AppHostFixture("search-ui",
    messageSink,
    TestAppHostOptions.Default with {
        ConfigureHost = (_, cfg) => {
            cfg.AddInMemory<MLSearchSettings>((x => x.IsEnabled, "true"),
                (x => x.IsInitialIndexingDisabled, "true"),
                (x => x.ChangedEntityIndexingDelay, "00:00:03"),
                (x => x.IndexingFlowResumeDelay, "00:00:01"));
        },
    });
