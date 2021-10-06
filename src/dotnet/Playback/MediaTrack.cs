using ActualChat.Media;

namespace ActualChat.Playback;

public record MediaTrack(
    Symbol Id,
    IMediaSource Source,
    Moment ZeroTimestamp)
    : IHasId<Symbol>
{ }
