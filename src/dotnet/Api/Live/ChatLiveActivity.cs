namespace ActualChat.Live;

/// <summary>
/// Who is live in a chat - a live session or people talking - as its list row and call button show it;
/// null when nothing is going on.
/// ParticipantCount counts live-session members when IsLiveSession, streaming talkers otherwise,
/// and is 0 only while a call is still dialing.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record ChatLiveActivity(
    [property: DataMember, Key(0)] int ParticipantCount,
    [property: DataMember, Key(1)] bool IsLiveSession,
    [property: DataMember, Key(2)] bool IsDialing = false)
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public bool HasLiveConversation => ParticipantCount > 0;
}
