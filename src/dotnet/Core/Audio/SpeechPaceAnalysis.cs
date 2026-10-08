namespace ActualChat.Audio;

public sealed record SpeechPaceAnalysis(
    SpeechPaceSegment[] Segments,
    int ValidWords,
    int RejectedWords,
    int UnclassifiedWords,
    int UnclassifiedMilliseconds,
    int PauseMilliseconds,
    int UnmappedMilliseconds);
