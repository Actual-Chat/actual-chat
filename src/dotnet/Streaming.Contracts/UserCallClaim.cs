using ActualChat.Live;

namespace ActualChat.Streaming;

/// <summary>
/// The server's claim on a user for the one call they're in, across every device they're signed in on.
/// It is what <see cref="ICallsBackend"/> stores; clients get its <see cref="UserCall"/>.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record UserCallClaim
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
    // The client that placed or answered the call: the only one GetMyCall names it to once it's past ringing.
    [DataMember(Order = 7), Key(7)]
    public string? SessionHash { get; init; }
    [DataMember(Order = 8), Key(8)]
    public string? ClientId { get; init; }
    [DataMember(Order = 9), Key(9)]
    public CallId CallId { get; init; } = null!;
    [DataMember(Order = 10), Key(10)]
    public CallOutcome Outcome { get; init; }

    public UserCall ToUserCall()
        => new() {
            ChatId = ChatId,
            Role = Role,
            Phase = Phase,
            PeerId = PeerId,
            HasVideo = HasVideo,
            CallId = CallId,
            Outcome = Outcome,
        };
}
