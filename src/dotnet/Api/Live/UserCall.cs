namespace ActualChat.Live;

public enum CallRole { Caller, Callee }

public enum CallPhase { Ringing, Dialing, Active }

/// <summary>
/// The one call a user is in, across every device they're signed in on: the server's claim on
/// that user, and the answer <see cref="ActualChat.Streaming.ILiveSessions"/> gives their clients.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record UserCall
{
    [DataMember(Order = 0), Key(0)]
    public ChatId ChatId { get; init; } = null!;
    [DataMember(Order = 1), Key(1)]
    public AuthorId AuthorId { get; init; } = null!;
    [DataMember(Order = 2), Key(2)]
    public CallRole Role { get; init; }
    [DataMember(Order = 3), Key(3)]
    public CallPhase Phase { get; init; }
    [DataMember(Order = 4), Key(4)]
    public AuthorId? PeerId { get; init; }
    [DataMember(Order = 5), Key(5)]
    public bool HasVideo { get; init; }
    [DataMember(Order = 6), Key(6)]
    public Moment SinceAt { get; init; }
}
