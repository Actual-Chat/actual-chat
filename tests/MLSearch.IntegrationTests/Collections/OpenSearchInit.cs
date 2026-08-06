using ActualChat.MLSearch.Engine;
using OpenSearch.Client;
using OpenSearch.Net;
using HttpMethod = OpenSearch.Net.HttpMethod;

namespace ActualChat.MLSearch.IntegrationTests;

// An instance of OpenSearchInit class is created via DI container on app start
internal sealed class OpenSearchInit(OpenSearchConfigurator openSearchConfigurator) : IModuleInitializer
{
    public Task Initialize(CancellationToken cancellationToken)
        => openSearchConfigurator.InitializeAsync(cancellationToken);
}

// An instance of OpenSearchCleanup class is created via DI container of the app host of MLSearchCollection above
internal sealed class OpenSearchCleanup(
    IOpenSearchClient openSearch,
    OpenSearchNames openSearchNames
    ) : IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        _ = await openSearch.LowLevel.DoRequestAsync<StringResponse>(
            HttpMethod.DELETE, $"/{openSearchNames.CommonIndexPattern}", CancellationToken.None);
        _ = await openSearch.LowLevel.DoRequestAsync<StringResponse>(
            HttpMethod.DELETE, $"/_template/{openSearchNames.CommonIndexPattern}", CancellationToken.None);
        _ = await openSearch.LowLevel.DoRequestAsync<StringResponse>(
            HttpMethod.DELETE, $"/_ingest/pipeline/{openSearchNames.CommonIndexPattern}", CancellationToken.None);
    }
}
