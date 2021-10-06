namespace ActualChat.Audio.WebM;

public enum WebMReadResultKind
{
    None = 0,
    Ebml,
    Segment,
    BeginCluster,
    CompleteCluster,
    Block,
    BlockGroup
}
