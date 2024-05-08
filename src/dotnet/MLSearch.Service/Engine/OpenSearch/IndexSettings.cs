
namespace ActualChat.MLSearch.Engine.OpenSearch;

internal sealed record PlainIndexSettings(string IndexName);
internal sealed record SemanticIndexSettings(string IndexName, string ModelId, string IngestPipelineId);
