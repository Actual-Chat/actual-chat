namespace ActualChat;

/// <summary>
/// Opts a command out of <c>ApiCommandDeduplicator</c>. Two kinds of command want this:
/// - Whose repeat is harmless but whose suppression isn't and which never resend the same
///   <see cref="ApiCommand.Uuid"/>, so they'd pay the dedup store's two round-trips for nothing;
/// - The ones that deliberately do resend it because they keep a durable receipt of their own,
///   which the dedup store's short-lived entries would shadow.
/// </summary>
public interface INotDeduplicated;
