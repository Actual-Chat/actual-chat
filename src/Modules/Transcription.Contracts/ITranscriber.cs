using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Stl.Fusion.Authentication;
using Stl.Serialization;

namespace ActualChat.Transcription
{
    public enum AudioFormat
    {
        WAV = 0,
        FLAC,
        OPUS
    }

    public enum AudioLanguage
    {
        English = 0,
        Russian,
        Spanish,
        Chinese,
        German
    }
    
    public record TranscriptionSession(Ulid Id)
    {
        public TranscriptionSession() : this(Ulid.NewUlid()) { }
    }

    public record StartTranscriptionSessionCommand(
        Session Session, 
        AudioFormat Format,
        AudioLanguage Language,
        AudioLanguage[] AlternativeLanguages,
        int NumberOfChannels,
        int SampleRate, 
        int MaxAlternatives,
        bool EnableDiarization,
        int DiarizationSpeakerCount,
        bool EnablePunctuation,
        ImmutableList<string> TranscriptionHistory)
    {
        public StartTranscriptionSessionCommand() 
            : this(
                Session.Null, 
                AudioFormat.WAV,
                AudioLanguage.English,
                Array.Empty<AudioLanguage>(),
                1,
                8000,
                3,
                false,
                1,
                true,
                ImmutableList<string>.Empty) { }
    }

    public record AppendAudioDataCommand(Session Session, Ulid TranscriptionSessionId, Base64Encoded Data)
    {
        public AppendAudioDataCommand() : this(Session.Null, Ulid.Empty, default)
        { }
    }

    public record EndTranscriptionSessionCommand(Session Session, Ulid TranscriptionSessionId)
    {
        public EndTranscriptionSessionCommand() : this(Session.Null, Ulid.Empty)
        { }
    }

    public record TranscriptionResult(Ulid TranscriptionSessionId, IImmutableList<Transcript> Alternatives)
    {
        public TranscriptionResult() : this (Ulid.Empty, ImmutableList<Transcript>.Empty)
        { }
    }

    public record Transcript(string Text, float Confidence, TimeSpan StartOffset, TimeSpan EndOffset,
        IImmutableList<Word> Words)
    {
        public Transcript() : this (string.Empty, 0, TimeSpan.Zero, TimeSpan.Zero, ImmutableList<Word>.Empty)
        { }
    }

    public record Word(string Text, TimeSpan StartOffset, TimeSpan EndOffset, string SpeakerId)
    {
        public Word() : this (string.Empty, TimeSpan.Zero, TimeSpan.Zero, string.Empty)
        { }
    }

    public record TranscriptionSessionStats(Ulid TranscriptionSessionId, TimeSpan Duration)
    {
        public TranscriptionSessionStats() : this (Ulid.Empty, TimeSpan.Zero)
        { }
    }

    public interface ITranscriber
    {
        Task<TranscriptionSession> StartSession(StartTranscriptionSessionCommand startSessionCommand, CancellationToken cancellationToken = default);

        Task<TranscriptionResult> Append(AppendAudioDataCommand appendAudioDataCommand, CancellationToken cancellationToken = default);

        Task<TranscriptionSessionStats> EndSession(EndTranscriptionSessionCommand endTranscriptionSessionCommand, CancellationToken cancellationToken = default);
    }
}