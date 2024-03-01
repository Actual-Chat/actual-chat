using System.Security.Cryptography;
using System.Text;
using OpenSearch.Client;

namespace ActualChat.MLSearch.Engine.OpenSearch;

public sealed class OpenSearchClusterSettings(string modelAllConfig, string modelId, int modelEmbeddingDimension)
{
    private const string NamePrefix = "ml";
    private const string IngestPipelineNameSuffix = "ingest-pipeline";
    private const string SearchIndexNameSuffix = "search-index";

    public string ModelId => modelId;
    public int ModelEmbeddingDimension => modelEmbeddingDimension;
    public Id IntoFullIngestPipelineId(string id)
        => new (string.Join('-', NamePrefix, id, IngestPipelineNameSuffix, IntoUniqueKey()));

    public string IntoFullSearchIndexId(string id)
        => string.Join('-', NamePrefix, id, SearchIndexNameSuffix, IntoUniqueKey());

    private string IntoUniqueKey()
    {
        // This method must generate a unique key per configuration.
        // This key is later used to namespace models and pipelines
        // on the backend cluster.
#pragma warning disable CA5350 // Do Not Use Weak Cryptographic Algorithms
        var hash = SHA1.HashData(Encoding.ASCII.GetBytes(modelAllConfig));
#pragma warning restore CA5350 // Do Not Use Weak Cryptographic Algorithms
        return Convert.ToHexString(hash).ToLower(CultureInfo.InvariantCulture);
    }
}
