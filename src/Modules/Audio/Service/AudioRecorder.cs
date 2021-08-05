using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using ActualChat.Storage;
using Stl.Fusion;
using Stl.Fusion.Authentication;
using Stl.Fusion.EntityFramework;
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
        private readonly ConcurrentDictionary<string, ConcurrentQueue<(Moment, Base64Encoded)>> _dataCapacitor;

        public AudioRecorder(IServiceProvider services, IAuthService authService, IBlobStorage blobStorage) : base(services)
        {
            _authService = authService;
            _blobStorage = blobStorage;
            _dataCapacitor = new ConcurrentDictionary<string, ConcurrentQueue<(Moment, Base64Encoded)>>();
        }

        public virtual async Task<Symbol> Initialize(InitializeAudioRecorderCommand command, CancellationToken cancellationToken = default)
        {
            if (Computed.IsInvalidating()) return default!;
           
            var user = await _authService.GetUser(command.Session, cancellationToken);
            user.MustBeAuthenticated();
            
            // @AY,. this code below doesn't work throwing exception
            //  await using var dbContext = await CreateCommandDbContext(cancellationToken);
            // at 
            // protected Task<TDbContext> CreateCommandDbContext(bool readWrite = true, CancellationToken cancellationToken = default)
            //    ...
            //    var operationScope = commandContext.Items.Get<DbOperationScope<TDbContext>>();
            // please check


            var recordingId = IdGenerator.NewSymbol();
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