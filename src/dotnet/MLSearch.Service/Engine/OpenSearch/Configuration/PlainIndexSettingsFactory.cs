
using ActualChat.MLSearch.Engine.OpenSearch.Setup;
using Microsoft.Extensions.Options;

namespace ActualChat.MLSearch.Engine.OpenSearch.Configuration;

internal sealed class PlainIndexSettingsFactory(OpenSearchNames openSearchNames, IClusterSetup clusterSetup)
    : IOptionsFactory<PlainIndexSettings>
{
    public PlainIndexSettings Create(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        var setupResult = clusterSetup.Result;
        var indexName = openSearchNames.GetFullName(name, setupResult.EmbeddingModelProps);
        return new PlainIndexSettings(indexName);
    }
}
