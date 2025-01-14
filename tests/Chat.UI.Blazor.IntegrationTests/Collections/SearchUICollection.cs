using ActualChat.MLSearch.Module;
using ActualChat.Testing.Host;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

[CollectionDefinition(nameof(SearchUICollection))]
public class SearchUICollection : ICollectionFixture<SearchAppHostFixture>;

public class SearchAppHostFixture(IMessageSink messageSink) : ActualChat.Testing.Host.AppHostFixture("search-ui",
    messageSink,
    TestAppHostOptions.Default with {
        ConfigureHost = (_, cfg) => {
            cfg.AddInMemoryCollection(($"{nameof(MLSearchSettings)}:{nameof(MLSearchSettings.IsEnabled)}", "true"))
                .AddInMemoryCollection(($"{nameof(MLSearchSettings)}:{nameof(MLSearchSettings.IsInitialIndexingDisabled)}", "true"))
                .AddInMemoryCollection(($"{nameof(MLSearchSettings)}:{nameof(MLSearchSettings.ChangedEntityIndexingDelay)}", "00:00:03"))
                .AddInMemoryCollection(($"{nameof(MLSearchSettings)}:{nameof(MLSearchSettings.IndexingFlowResumeDelay)}", "00:00:01"));
        },
    });
