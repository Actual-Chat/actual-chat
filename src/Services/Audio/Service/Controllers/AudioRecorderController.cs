using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Authentication;
using Stl.Fusion.Server;
using Stl.Text;

namespace ActualChat.Audio.Controllers
{
    [Route("api/audio-recorders")]
    [ApiController, JsonifyErrors]
    public class AudioRecorderController : ControllerBase, IAudioRecorder
    {
        private readonly IAudioRecorder _audioRecorder;
        private readonly ISessionResolver _sessionResolver;

        public AudioRecorderController(IAudioRecorder audioRecorder, ISessionResolver sessionResolver)
        {
            _audioRecorder = audioRecorder;
            _sessionResolver = sessionResolver;
        }

        [HttpPost]
        public Task<Symbol> Initialize([FromBody] InitializeAudioRecorderCommand command, CancellationToken cancellationToken = default)
        {
            command.UseDefaultSession(_sessionResolver);
            return _audioRecorder.Initialize(command, cancellationToken);
        }

        [HttpPost("append")]
        public Task AppendAudio([FromBody] AppendAudioCommand command, CancellationToken cancellationToken = default)
        {
            return _audioRecorder.AppendAudio(command, cancellationToken);
        }

        [HttpDelete]
        public Task Complete([FromBody] CompleteAudioRecording command, CancellationToken cancellationToken = default)
        {
            return _audioRecorder.Complete(command, cancellationToken);
        }
    }
}