namespace ActualChat.Media;

public interface IMediaStreamPart
{
    MediaFormat? Format { get; }
    MediaFrame? Frame { get; }
}

public interface IMediaStreamPart<TFormat, TFrame> : IMediaStreamPart
{
    new TFormat? Format { get; init; }
    new TFrame? Frame { get; init; }
}
