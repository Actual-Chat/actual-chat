using System.Reactive;
using ActualChat.Audio;
using Stl.CommandR;
using Stl.Serialization;
using Stl.Text;

namespace ActualChat.Transcription
{
    public record BeginTranscriptionCommand : ICommand<Symbol>
    {
        public TranscriptionOptions Options { get; init; } = new();
        public AudioFormat AudioFormat { get; init; } = new();
        public Base64Encoded Data { get; init; } = default;
    }

    public record AppendTranscriptionCommand(Symbol TranscriptId, Base64Encoded Data) : ICommand<Unit>
    {
        public AppendTranscriptionCommand() : this(Symbol.Empty, default) { }
    }

    public record EndTranscriptionCommand(Symbol TranscriptId) : ICommand<Unit>
    {
        public EndTranscriptionCommand() : this(Symbol.Empty) { }
    }
}
