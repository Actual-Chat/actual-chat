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
        public Moment ClientStartTime { get; init; } = CpuClock.Now;
    }

    public record AppendAudioCommand(Symbol RecordingId, Moment ClientOffset, Base64Encoded Data) : ICommand<Unit>
    { }
}
