using System.Collections.Immutable;
using System.Reactive;
using ActualChat.Audio;
using Stl.CommandR;
using Stl.Serialization;
using Stl.Text;

namespace ActualChat.Transcription
{
    public record BeginTranscriptionCommand : ICommand<Symbol>
    {
        public Symbol RecordId { get; init; } = Symbol.Empty;
        public TranscriptionOptions Options { get; init; } = new();
        public AudioFormat AudioFormat { get; init; } = new();

        public void Deconstruct(out Symbol recordId, out TranscriptionOptions options, out AudioFormat format)
        {
            recordId = RecordId;
            options = Options;
            format = AudioFormat;
        }
    }

    public record AppendTranscriptionCommand(Symbol TranscriptId, Base64Encoded Data) : ICommand<Unit>
    {
        public AppendTranscriptionCommand() : this(Symbol.Empty, default) { }
    }

    public record EndTranscriptionCommand(Symbol TranscriptId) : ICommand<Unit>
    {
        public EndTranscriptionCommand() : this(Symbol.Empty) { }
    }

    public record AckTranscriptionCommand(Symbol TranscriptId, int Index) : ICommand<Unit>
    {
        public AckTranscriptionCommand() : this(Symbol.Empty, default) { }
    }

    public record PollTranscriptionCommand(Symbol TranscriptId, int Index) : ICommand<ImmutableArray<TranscriptFragment>>
    {
        public PollTranscriptionCommand() : this(Symbol.Empty, default) { }
    }

    public record PollResult(bool ContinuePolling, ImmutableArray<TranscriptFragmentVariant> Fragments);
}
