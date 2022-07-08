using System.Buffers;

namespace ActualChat.Blobs;

public static class ReadOnlySequenceExt
{
    public static ReadOnlySequence<T> Append<T>(this ReadOnlySequence<T> source, ReadOnlyMemory<T> chunk)
    {
        if (chunk.IsEmpty)
            return source;

        if (source.IsEmpty) {
            var segment = new MemorySegment<T>(chunk);
            return new ReadOnlySequence<T>(segment, 0, segment, chunk.Length);
        }

        var start = source.Start;
        var end = source.End;
        var startSegment = start.GetObject() as MemorySegment<T>;
        var startIndex = start.GetInteger();
        var endSegment = end.GetObject() as MemorySegment<T>;
        if (startSegment == null || endSegment == null)
            throw new InvalidOperationException("ReadOnlySequence.Append supports only MemorySegment-based sources.");

        var newEndSegment = endSegment.Append(chunk);
        return new ReadOnlySequence<T>(startSegment, startIndex, newEndSegment, chunk.Length);
    }
}
