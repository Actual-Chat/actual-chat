namespace ActualChat.Audio;

[StructLayout(LayoutKind.Auto)]
public readonly record struct TimedSpeechWord(Range<int> TextRange, Range<int> TimeRange);
