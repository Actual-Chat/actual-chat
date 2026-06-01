namespace ActualChat.Live;

/// <summary>
/// How a user takes part in a live conversation. Any active kind counts as "joined".
/// </summary>
public enum ParticipationKind
{
    AudioListen = 0,
    VideoView = 1,
    Record = 2,
}
