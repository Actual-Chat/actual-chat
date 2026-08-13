namespace ActualChat.Live;

/// <summary>
/// How much talking actually happened in a chat's live session recently: per-author speech
/// seconds and transcribed characters over a trailing window, plus the session's own age.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record ConversationStats
{
    [DataMember(Order = 0), Key(0)]
    public TimeSpan Duration { get; init; }
    [DataMember(Order = 1), Key(1)]
    public ApiMap<AuthorId, double> SpeechDurations { get; init; } = new();
    [DataMember(Order = 2), Key(2)]
    public ApiMap<AuthorId, int> TranscriptSizes { get; init; } = new();

    public TimeSpan GetSpeechDuration(AuthorId? exceptAuthorId)
        => TimeSpan.FromSeconds(SpeechDurations.Where(kv => kv.Key != exceptAuthorId).Sum(kv => kv.Value));

    public int GetTranscriptSize(AuthorId? exceptAuthorId)
        => TranscriptSizes.Where(kv => kv.Key != exceptAuthorId).Sum(kv => kv.Value);
}
