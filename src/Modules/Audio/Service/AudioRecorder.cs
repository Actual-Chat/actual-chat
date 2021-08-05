using System;
using System.Threading;
using System.Threading.Tasks;
using Stl.Fusion;
using Stl.Fusion.Authentication;
using Stl.Text;

namespace ActualChat.Audio
{
    [RegisterComputeService(typeof(IAudioRecorder))]
    public class AudioRecorder : IAudioRecorder
    {
        private readonly IAuthService _authService;

        public AudioRecorder(IAuthService authService)
        {
            _authService = authService;
        }

        public virtual async Task<Symbol> Initialize(InitializeAudioRecorderCommand command, CancellationToken cancellationToken = default)
        {
            if (Computed.IsInvalidating()) return default!;
            
            var user = await _authService.GetUser(command.Session, cancellationToken);
            user.MustBeAuthenticated();

            return Ulid.NewUlid().ToString();
        }

        public virtual async Task AppendAudio(AppendAudioCommand command, CancellationToken cancellationToken = default)
        {
            if (Computed.IsInvalidating()) return;
            
            var (recordingId, offset, data) = command;

            await Task.CompletedTask;
        }
    }
}