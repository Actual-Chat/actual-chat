namespace ActualChat.Live;

public enum CallRole { Caller, Callee }

public enum CallPhase { Ringing, Dialing, Active, Ended }

/// <summary>
/// The one call a user is in, as <see cref="ActualChat.Streaming.ILiveSessions"/> tells the client that
/// placed or answered it - or, while it rings, every client of theirs. An <see cref="CallPhase.Ended"/>
/// one is how that client learns the call is over.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record UserCall
{
    // The keys skipped are the server's claim's own, so a client built before they left still reads this.
    [DataMember(Order = 0), Key(0)]
    public ChatId ChatId { get; init; } = null!;
    [DataMember(Order = 2), Key(2)]
    public CallRole Role { get; init; }
    [DataMember(Order = 3), Key(3)]
    public CallPhase Phase { get; init; }
    [DataMember(Order = 4), Key(4)]
    public AuthorId? PeerId { get; init; }
    [DataMember(Order = 5), Key(5)]
    public bool HasVideo { get; init; }
    [DataMember(Order = 9), Key(9)]
    public CallId CallId { get; init; } = null!;
    // How the call ended, once it has: None when the server only knows it is gone.
    [DataMember(Order = 10), Key(10)]
    public CallOutcome Outcome { get; init; }
}
