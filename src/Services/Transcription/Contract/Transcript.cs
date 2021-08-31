using System;
using System.Collections.Immutable;
using System.Text.Json.Serialization;
using ActualChat.Audio;
using Stl.Serialization;
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

        public int TextIndex { get; init; } = 0;
        public string SpeakerId { get; init; } = "";
        public double Confidence { get; init; } = 1;
        public bool IsFinal { get; init; }
    }

    public record TranscriptErrorFragment(int Code, string Message) : TranscriptFragment;

    [Serializable]
    public class TranscriptFragmentVariant : Variant<TranscriptFragment>
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull), Newtonsoft.Json.JsonIgnore]
        public TranscriptSpeechFragment? Speech { get => Get<TranscriptSpeechFragment>(); init => Set(value); }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull), Newtonsoft.Json.JsonIgnore]
        public TranscriptSilenceFragment? Silence { get => Get<TranscriptSilenceFragment>(); init => Set(value); }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull), Newtonsoft.Json.JsonIgnore]
        public TranscriptOtherAudioFragment? Other { get => Get<TranscriptOtherAudioFragment>(); init => Set(value); }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull), Newtonsoft.Json.JsonIgnore]
        public TranscriptErrorFragment? Error { get => Get<TranscriptErrorFragment>(); init => Set(value); }

        [JsonConstructor]
        public TranscriptFragmentVariant() { }
        [Newtonsoft.Json.JsonConstructor]
        public TranscriptFragmentVariant(TranscriptFragment? value) : base(value) { }
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
