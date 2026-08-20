using ActualChat.Kvas;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// What <see cref="ChatUI"/>'s tail prefetcher has already pulled into the offline cache: every chat
/// touched no later than <see cref="Boundary"/>, plus the ones above it and the tail each reached.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record ChatTailPrefetchState : IHasKvasKey<ChatTailPrefetchState>
{
    [DataMember, Key(0)] public Moment Boundary { get; init; }
    [DataMember, Key(1)] public ApiArray<ChatTailPrefetch> Chats { get; init; } = [];
}

[DataContract, MessagePackObject]
public sealed partial record ChatTailPrefetch(
    [property: DataMember, Key(0)] ChatId ChatId,
    [property: DataMember, Key(1)] Moment TouchedAt,
    [property: DataMember, Key(2)] long EntryLid);
