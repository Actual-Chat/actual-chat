namespace ActualChat.Transcription;

[Flags]
public enum TranscriptFlags : byte
{
    Stable = 1,
    Diff = 0x80,
}
