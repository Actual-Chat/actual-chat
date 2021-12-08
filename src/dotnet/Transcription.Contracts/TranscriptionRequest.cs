using ActualChat.Audio;

namespace ActualChat.Transcription;

public record TranscriptionRequest(
    string StreamId,
    AudioFormat Format,
    TranscriptionOptions Options);
