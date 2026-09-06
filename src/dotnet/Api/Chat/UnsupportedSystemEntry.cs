namespace ActualChat.Chat;

/// <summary>
/// Stands in for a <see cref="SystemEntry"/> kind this build has no member for. Produced on read
/// and consumed locally — deliberately not a <c>[Union]</c> member, so an attempt to write one
/// fails loudly instead of inventing a tag.
/// </summary>
public sealed record UnsupportedSystemEntry(ChatEntryId Id, long Version = 0) : SystemEntry(Id, Version);
