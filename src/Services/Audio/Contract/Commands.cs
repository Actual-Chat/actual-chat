using System.Reactive;
using Stl.CommandR;
using Stl.Serialization;
using Stl.Text;
using Stl.Time;
using Stl.Fusion.Authentication;

namespace ActualChat.Audio
{
    public record InitializeAudioRecorderCommand : ISessionCommand<Symbol>
    {
        public Session Session { get; init; } = Session.Null;
        public AudioFormat AudioFormat { get; init; } = new();
        public string Language { get; init; } = "en-us";
        public Moment ClientStartOffset { get; init; } = CpuClock.Now;
    }

    public record AppendAudioCommand(Symbol RecordingId, int Index, Moment ClientEndOffset, Base64Encoded Data) : ICommand<Unit>
    { }

    public record CompleteAudioRecording(Symbol RecordingId) : ICommand<Unit>
    { }
}
