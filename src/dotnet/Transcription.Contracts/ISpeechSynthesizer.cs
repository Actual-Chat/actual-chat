using ActualChat.Audio;

namespace ActualChat.Transcription;

public sealed record SpeechSynthesisOptions(Language Language, string? VoiceId = null);

/// <summary>
/// Speaks a stream of text chunks as 20 ms Opus <see cref="AudioFrame"/>s (48 kHz mono) emitted at
/// wall-clock pace with contiguous offsets from zero; gaps between chunks come out as silence.
/// </summary>
public interface ISpeechSynthesizer
{
    Task Synthesize(
        string streamId,
        ChannelReader<string> text,
        SpeechSynthesisOptions options,
        ChannelWriter<AudioFrame> output,
        CancellationToken cancellationToken = default);
}
