using System.Runtime.Intrinsics.Arm;
using System.Security.Cryptography;
using System.Text;
using OpenSearch.Client;

namespace ActualChat.MLSearch.SearchEngine.OpenSearch;

public sealed class OpenSearchClusterSettings(string modelAllConfig, string modelId, int modelEmbeddingDimension)
{
    private const string SearchIndexNamePrefix = "ml-search-index-";
    private const string IngestPipelineNamePrefix = "ml-ingest-pipeline-";
    private const string IngestCursorIndexNamePrefix = "ml-ingest-cursor-index-";

    public string ModelId => modelId;
    public int ModelEmbeddingDimension => modelEmbeddingDimension;
    public Id IntoIngestPipelineId()
        => new (IntoIngestPipelineName());

    public string IntoIngestPipelineName()
        => IngestPipelineNamePrefix + IntoUniqueKey();

    public IndexName IntoCursorIndexName()
        => IngestCursorIndexNamePrefix + IntoUniqueKey();

    public string IntoSearchIndexId()
        => SearchIndexNamePrefix + IntoUniqueKey();

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
