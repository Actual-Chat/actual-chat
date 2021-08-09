using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ActualChat.Audio.Db;
using Microsoft.Extensions.Logging;
using Stl.Fusion;
using Stl.Fusion.Authentication;
using Stl.Fusion.EntityFramework;
using Stl.Generators;
using Stl.Serialization;
using Stl.Text;
using Stl.Time;

namespace ActualChat.Audio
{
    public class AudioRecorder : DbServiceBase<AudioDbContext>, IAudioRecorder
    {
        private static readonly TimeSpan CleanupInterval = new TimeSpan(0, 0, 10);
        private static readonly TimeSpan SegmentLength = new TimeSpan(0, 1, 0);

        private readonly IAuthService _authService;
        private readonly Generator<string> _idGenerator;
        // AY:
        // - ConcurrentQueue is a bit of excess here, I guess - a normal queue would be fine
        // - It requires robust maintenance (mem leaks are easy to happen here), so
        //   prob. it's ok to just write for now
        private readonly ConcurrentDictionary<Symbol, Queue<(int, Moment, Base64Encoded)>> _dataCapacitor;

        public AudioRecorder(IServiceProvider services, IAuthService authService) : base(services)
        {
            _authService = authService;
            _idGenerator = new RandomStringGenerator(16, RandomStringGenerator.Base32Alphabet);
            _dataCapacitor = new ConcurrentDictionary<Symbol, Queue<(int, Moment, Base64Encoded)>>();
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
            dbContext.AudioRecordings.Add(new DbAudioRecording {
                Id = recordingId,
                UserId = user.Id,
                RecordingStartedUtc = command.ClientStartOffset,
                AudioCodec = command.AudioFormat.Codec,
                ChannelCount = command.AudioFormat.ChannelCount,
                SampleRate = command.AudioFormat.SampleRate,
                Language = command.Language,
                RecordingDuration = 0
            });

            return recordingId;
        }

        public virtual async Task AppendAudio(AppendAudioCommand command, CancellationToken cancellationToken = default)
        {
            if (Computed.IsInvalidating()) return;

            var (recordingId, index, offset, data) = command;
            Log.LogTrace($"{nameof(AppendAudio)}, RecordingId = {{RecordingId}}, Index = {{Index}}, DataLength = {{DataLength}}",
                recordingId.Value,
                command.Index,
                command.Data.Count);

            // Push to real-time pipeline
            // TBD


            // Buffering...
            _dataCapacitor.AddOrUpdate(
                recordingId,
                r => new Queue<(int, Moment, Base64Encoded)>(new []{ (index, offset, data) }),
                (r, q) => {
                    q.Enqueue((index, offset, data));
                    return q;
                }
            );

            await Task.CompletedTask;
        }

        private async Task BackgroundCycle(CancellationToken cancellationToken = default)
        {
            while (true) {
                await Task.Delay(CleanupInterval, cancellationToken);
                if (cancellationToken.IsCancellationRequested) return;

                await FlushBufferedSegments();
            }
        }

        private Task FlushBufferedSegments()
        {
            foreach (var (recordingId, queue) in _dataCapacitor) {
                // we definitely will have multi-threading issues with this implementation .. will do something

                // if (queue.TryPeek(out var tuple)) {
                //     var (index, offset, data) = tuple;
                //
                // }
            }
            return Task.CompletedTask;
        }
    }
}
