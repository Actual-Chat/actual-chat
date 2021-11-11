using ActualChat.Audio;

namespace ActualChat.Transcription;

public record TranscriptionRequest(
    StreamId StreamId,
    AudioFormat Format,
    TranscriptionOptions Options);
