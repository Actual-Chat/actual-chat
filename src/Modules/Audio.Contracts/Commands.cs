using System.Reactive;
using Stl.CommandR;
using Stl.Serialization;
using Stl.Text;
using Stl.Time;

namespace ActualChat.Audio
{
    public record InitializeAudioRecorderCommand : ICommand<Symbol>
    {
        public AudioFormat AudioFormat { get; init; } = new();
        public string Language { get; init; } = "en-us";
        public Moment RecordingStart { get; init; } = Moment.EpochStart;
    }
 
    public record AppendAudioCommand : ICommand<Unit>
    {
        public Symbol RecorderId { get; init; } = Symbol.Empty;
        public Moment Offset { get; init; } = Moment.EpochStart;
        public Base64Encoded Data { get; init; } = default;
    }
}