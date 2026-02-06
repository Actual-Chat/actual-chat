namespace ActualChat.Media;

/// <summary>
/// Represents a part of a media stream (either format metadata or a frame).
/// </summary>
public interface IMediaStreamPart
{
    MediaFormat? Format { get; }
    MediaFrame? Frame { get; }
}

/// <summary>
/// Typed version of <see cref="IMediaStreamPart"/> for specific format and frame types.
/// </summary>
public interface IMediaStreamPart<TFormat, TFrame> : IMediaStreamPart
{
    new TFormat? Format { get; init; }
    new TFrame? Frame { get; init; }
}
