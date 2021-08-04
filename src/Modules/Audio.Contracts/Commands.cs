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
        public Moment RecordingStart { get; init; } = Moment.EpochStart;
    }
 
    public record AppendAudioCommand(Moment Offset, Base64Encoded Data) : ICommand<Unit>
    { }
}