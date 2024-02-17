using ActualChat.Audio.WebM.Models;

namespace ActualChat.Audio.WebM;

public sealed class WebMDocumentBuilder
{
    private readonly List<Cluster> _clusters = new(4);

    private EBML? _ebml;
    private Segment? _segment;

    public WebMDocumentBuilder SetHeader(EBML ebml)
    {
        _ebml = ebml ?? throw new ArgumentNullException(nameof(ebml));
        return this;
    }

    public WebMDocumentBuilder SetSegment(Segment segment)
    {
        _segment = segment ?? throw new ArgumentNullException(nameof(segment));
        return this;
    }

    public WebMDocumentBuilder AddCluster(Cluster cluster)
    {
        if (cluster == null) throw new ArgumentNullException(nameof(cluster));

        _clusters.Add(cluster);
        return this;
    }


    public WebMDocument ToDocument()
    {
        var ebml = _ebml ?? throw new InvalidOperationException("EBML header is null.");
        var segment = _segment ?? throw new InvalidOperationException("Segment is null.");

        return new WebMDocument(ebml, segment, _clusters);
    }
}
