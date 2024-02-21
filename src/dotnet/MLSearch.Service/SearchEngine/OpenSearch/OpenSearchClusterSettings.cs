using OpenSearch.Client;

namespace ActualChat.MLSearch.SearchEngine.OpenSearch;

public sealed class OpenSearchClusterSettings
{
    public string OpenSearchClusterUri { get; set; } = "";
    public string? ModelId { get; set; }
    public int ModelDimension { get; set; }
    public string IngestPipelineName { get; set; } = "ml-ingest-pipeline";
    public string SearchIndexName { get; set; } = "ml-search-index";

    public Id IntoIngestPipelineId()
        => new (IngestPipelineName + IntoUniqueKey());

    public string IntoSearchIndexId()
        => SearchIndexName + IntoUniqueKey();
    public string IntoUniqueKey()
    {
        // This method must generate a unique key per configuration.
        // This key is later used to namespace models and pipelines
        // on the backend cluster.

        // TODO: Use sha256 hash instead.

        // ReSharper disable once ArrangeMethodOrOperatorBody
        return this.GetHashCode().ToString("D", CultureInfo.InvariantCulture);
    }
}
