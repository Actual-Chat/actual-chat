using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using ActualChat.Audio.Db;
using ActualChat.Storage;
using Microsoft.Extensions.Logging;
using Stl.Fusion;
using Stl.Fusion.Authentication;
using Stl.Fusion.EntityFramework;
using Stl.Generators;
using Stl.Generators.Internal;
using Stl.Serialization;
using Stl.Text;
using Stl.Time;

namespace ActualChat.Audio
{
    [RegisterComputeService(typeof(IAudioRecorder))]
    public class AudioRecorder : DbServiceBase<AudioDbContext>, IAudioRecorder
    {
        private readonly IAuthService _authService;
        private readonly IBlobStorage _blobStorage;
        private readonly Generator<string> _idGenerator;
        // AY:
        // - ConcurrentQueue is a bit of excess here, I guess - a normal queue would be fine
        // - It requires robust maintenance (mem leaks are easy to happen here), so
        //   prob. it's ok to just write for now
        private readonly ConcurrentDictionary<Symbol, ConcurrentQueue<(Moment, Base64Encoded)>> _dataCapacitor;

        public AudioRecorder(IServiceProvider services, IAuthService authService, IBlobStorage blobStorage) : base(services)
        {
            _authService = authService;
            _blobStorage = blobStorage;
            _idGenerator = new RandomStringGenerator(16, RandomStringGenerator.Base32Alphabet);
            _dataCapacitor = new ConcurrentDictionary<Symbol, ConcurrentQueue<(Moment, Base64Encoded)>>();
        }

        public virtual async Task<Symbol> Initialize(InitializeAudioRecorderCommand command, CancellationToken cancellationToken = default)
        {
            if (Computed.IsInvalidating()) return default!;

            var user = await _authService.GetUser(command.Session, cancellationToken);
            user.MustBeAuthenticated();

            // AY: This requires OF services & Operations table
            await using var dbContext = await CreateCommandDbContext(cancellationToken);

            var recordingId = _idGenerator.Next();
            Log.LogInformation($"{nameof(Initialize)}, RecordingId = {{RecordingId}}", recordingId);
            return recordingId;
        }

        public virtual async Task AppendAudio(AppendAudioCommand command, CancellationToken cancellationToken = default)
        {
            if (Computed.IsInvalidating()) return;

            var (recordingId, offset, data) = command;
            // _dataCapacitor.AddOrUpdate(
            //     recordingId,
            //     (r, q) => { return null },
            //     ()
            // );

            await Task.CompletedTask;
        }
    }
}
