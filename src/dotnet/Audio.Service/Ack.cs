namespace ActualChat.Audio;

public record Ack(AckType Type, StreamType StreamType, string StreamId);

public enum StreamType
{
    Audio = 1,
    Transcription,
}

public enum AckType
{
    Received = 1,
    Completed,
}
