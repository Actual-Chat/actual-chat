namespace ActualChat.Live;

/// <summary>
/// What a chat list row shows about a chat's live activity, in one RPC call:
/// see <c>ILiveSessions.GetActivitySummary</c>.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record ChatActivitySummary(
    [property: DataMember, Key(0)] ChatLiveActivity? LiveActivity,
    // Everyone typing but the caller, in the order they started
    [property: DataMember, Key(1)] ApiArray<AuthorId> TypingAuthorIds,
    // Audio or video streams, or a recorder - the server half of ChatActivityUI.HasOngoingCall
    [property: DataMember, Key(2)] bool HasStreams)
{
    public static readonly ChatActivitySummary None = new(null, default, false);
}
