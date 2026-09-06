namespace ActualChat.Chat;

/// <summary>
/// Stands in for a system entry this build has no member for — an unknown <c>[Union]</c> tag on
/// the wire, or a row whose option the database reader doesn't recognize. It carries the base
/// prefix and nothing else, and renders as nothing at all.
/// </summary>
/// <remarks>
/// A registered member rather than a read-only placeholder, because a server that rolled back
/// past the release which wrote the row still has to serve it: it reads one of these out of the
/// database and fans it out. A peer that doesn't know tag 100 classifies it as a system entry and
/// builds its own placeholder, which is the same value.
/// </remarks>
[DataContract, MessagePackObject]
public sealed partial record UnsupportedSystemEntry : SystemEntry
{
    public UnsupportedSystemEntry() : base((ChatEntryId)null!) { }

    [SerializationConstructor]
    public UnsupportedSystemEntry(ChatEntryId id, long version = 0) : base(id, version) { }
}
