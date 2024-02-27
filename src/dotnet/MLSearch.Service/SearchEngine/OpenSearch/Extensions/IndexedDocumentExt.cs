using OpenSearch.Client;

namespace ActualChat.MLSearch.SearchEngine.OpenSearch.Extensions;

internal static class IndexedDocumentExt
{
    // TODO: Check how feasible is it to move this logic into ingest pipelines.
    // Note: Intent to have a separate method.
    // This is a temporary method till we implement ingest pipeline
    // that would make document uri unique and add int into a separate
    // lookup index. This pipeline should get document id from that index
    // aswell. This will eliminate this method entirely.
    // Note: OpenSearch _id key has a limit of 512 bytes string.
    // Note: Moving this into ingest pipeline would require same logic applied for deletions.
    public static Id Id(this IndexedDocument document)
        => new (document.Uri);
}
