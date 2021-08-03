using System.Collections.Immutable;
using ActualChat.Audio;
using Stl.Text;

namespace ActualChat.Transcription
{
    public record Transcript(
        Symbol TranscriptId,
        TranscriptionOptions Options,
        AudioFormat AudioFormat,
        ImmutableList<TranscriptFragment> Fragments,
        TranscriptSummary Summary,
        TranscriptAudioSummary AudioSummary)
    {
        public Transcript()
            : this(Symbol.Empty, null!, null!, ImmutableList<TranscriptFragment>.Empty, null!, null!) { }
    }

    public record TranscriptFragment
    {
        public int Index { get; init; }
        public double StartOffset { get; init; }
        public double Duration { get; init; }
    }

    public record TranscriptSilenceFragment : TranscriptFragment
    { }

    public record TranscriptOtherAudioFragment : TranscriptFragment
    { }

    public record TranscriptSpeechFragment : TranscriptFragment
    {
        public string Text { get; init; } = "";
        public string SpeakerId { get; init; } = "";
        public double Confidence { get; init; } = 1;
    }

    // Summaries

    public record TranscriptSummary
    {
        public double Duration { get; init; }
        public double Confidence { get; init; }
        public int FragmentCount { get; init; }
    }

    public record TranscriptAudioSummary
    {
        public Symbol AudioId { get; init; }
        public double Duration { get; init; }
    }
}
