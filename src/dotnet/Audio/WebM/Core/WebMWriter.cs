using ActualChat.Audio.WebM.Models;
using ActualChat.Spans;

namespace ActualChat.Audio.WebM;

[StructLayout(LayoutKind.Sequential)]
public ref struct WebMWriter
{
    private SpanWriter _spanWriter;

    public WebMWriter(Span<byte> span, bool segmentHasUnknownSize = true, bool clusterHasUnknownSize = true)
    {
        _spanWriter = new SpanWriter(span);

        SegmentHasUnknownSize = segmentHasUnknownSize;
        ClusterHasUnknownSize = clusterHasUnknownSize;
    }

    public bool SegmentHasUnknownSize { get; }
    public bool ClusterHasUnknownSize { get; }

    public ReadOnlySpan<byte> Written => _spanWriter.Span[.._spanWriter.Position];

    public int Position => _spanWriter.Position;

    public bool Write(BaseModel entry)
    {
        if (entry == null)
            throw new ArgumentNullException(nameof(entry));

        var beforePosition = _spanWriter.Position;
        var success = entry.Write(ref _spanWriter);
        if (success) return true;

        _spanWriter.Position = beforePosition;
        return false;
    }
}
