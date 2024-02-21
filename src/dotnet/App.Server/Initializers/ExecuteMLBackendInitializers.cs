using ActualChat.Hosting;
using ActualChat.MLSearch.Module;
using ActualChat.MLSearch.SearchEngine.OpenSearch;
using OpenSearch.Client;

namespace ActualChat.App.Server.Initializers;

public class ExecuteMLBackendInitializers(IServiceProvider services): IServiceInitializer
{
    public async Task Invoke(CancellationToken cancellationToken)
        => await new OpenSearchClusterSetup(
                services.GetRequiredService<IOpenSearchClient>(),
                services.GetRequiredService<MLSearchSettings>().OpenSearchClusterSettings,
                services.LogFor<OpenSearchClusterSetup>()
            ).Run()
            .ConfigureAwait(false);
}
