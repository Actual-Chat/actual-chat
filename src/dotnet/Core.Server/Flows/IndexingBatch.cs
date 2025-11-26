using System.Runtime.InteropServices;

namespace ActualChat.Flows;

/// <summary>
/// Result of processing a batch in an indexing flow.
/// </summary>
/// <typeparam name="TCursor">Type of the cursor tracking indexing progress.</typeparam>
/// <param name="IsEmpty">True if no items were found in this batch.</param>
/// <param name="IsTailReached">True if we've caught up with the data source (no more immediate work).</param>
/// <param name="NextCursor">Updated cursor position after processing this batch.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct IndexingBatch<TCursor>(
    bool IsEmpty,
    bool IsTailReached,
    TCursor? NextCursor);
