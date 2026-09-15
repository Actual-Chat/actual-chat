using ActualChat.Audio;
using ActualChat.Chat;

namespace ActualChat.Transcription;

public sealed record SpeechSynthesisOptions(Language Language, string? VoiceId = null);

/// <summary>
/// Speaks a stream of text chunks as 20 ms Opus <see cref="AudioFrame"/>s (48 kHz mono) emitted at
/// wall-clock pace with contiguous offsets from zero, or one text as a whole, unpaced, as an
/// <see cref="AudioSource"/>; gaps between chunks come out as silence.
/// </summary>
public interface ISpeechSynthesizer
{
    Task Synthesize(
        string streamId,
        ChannelReader<string> text,
        SpeechSynthesisOptions options,
        ChannelWriter<AudioFrame> output,
        CancellationToken cancellationToken = default);

    Task<AudioSource> Synthesize(
        string text,
        SpeechSynthesisOptions options,
        CancellationToken cancellationToken = default);

    // MP3 is what a browser plays straight from a URL; used for voice previews only
    Task<byte[]> SynthesizeMp3(
        string text,
        SpeechSynthesisOptions options,
        CancellationToken cancellationToken = default);

    // The stock voices a speaker can pick from, sorted by gender then id; empty when unknown
    Task<ApiArray<DubVoice>> ListVoices(CancellationToken cancellationToken = default);
}
