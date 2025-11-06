namespace ActualChat.Audio.APM;

[StructLayout(LayoutKind.Auto)]
public readonly record struct StreamConfig(int SampleRateHz, int Channels);
