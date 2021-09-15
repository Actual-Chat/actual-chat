using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ActualChat.Audio.Orchestration;
using ActualChat.Chat;
using ActualChat.Streaming;
using ActualChat.Transcription;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Stl.CommandR;
using Stl.Text;

namespace ActualChat.Audio
{
    public class AudioOrchestrator : BackgroundService
    {
        private readonly ITranscriber _transcriber;
        private readonly AudioSaver _audioSaver;
        private readonly IServerSideRecorder<AudioRecord> _audioRecorder;
        private readonly AudioActivityExtractor _audioActivityExtractor;
        private readonly IServerSideStreamer<BlobPart> _blobStreamer;
        private readonly IServerSideStreamer<TranscriptPart> _transcriptStreamer;
        private readonly IServerSideChatService _chat;
        private readonly ILogger<AudioOrchestrator> _log;

        public static bool SkipAutoStart { get; set; } = true;

        public AudioOrchestrator(
            ITranscriber transcriber,
            AudioSaver audioSaver,
            IServerSideRecorder<AudioRecord> audioRecorder,
            AudioActivityExtractor audioActivityExtractor,
            IServerSideStreamer<BlobPart> blobStreamer,
            IServerSideStreamer<TranscriptPart> transcriptStreamer,
            IServerSideChatService chat,
            ILogger<AudioOrchestrator> log)
        {
            _transcriber = transcriber;
            _audioSaver = audioSaver;
            _audioRecorder = audioRecorder;
            _audioActivityExtractor = audioActivityExtractor;
            _blobStreamer = blobStreamer;
            _transcriptStreamer = transcriptStreamer;
            _chat = chat;
            _log = log;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (SkipAutoStart)
                return;

            while (true) {
                // TODO(AK): add push-back based on current node performance metrics \ or provide signals for scale-out
                var recording = await WaitForNewRecording(stoppingToken);
                if (stoppingToken.IsCancellationRequested)
                    return;

                _ = StartAudioPipeline(recording!, stoppingToken);
            }
        }

        internal async Task<AudioRecord?> WaitForNewRecording(CancellationToken cancellationToken)
        {
            while (true) {
                var recording = await _audioRecorder.WaitForNewRecording(cancellationToken);
                if (recording != null)
                    return recording;
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        internal async Task StartAudioPipeline(AudioRecord audioRecord, CancellationToken cancellationToken)
        {
            var audioReader = await _audioRecorder.GetRecording(audioRecord.Id, cancellationToken);
            var segments = _audioActivityExtractor.GetSegmentsWithAudioActivity(audioRecord, audioReader);
            await foreach (var segment in segments.WithCancellation(cancellationToken)) {
                var distributeStreamTask = DistributeAudioStream(segment, cancellationToken);
                var chatEntryTask = PublishChatEntry(segment, cancellationToken);
                _ = TranscribeStreamEntry(segment, cancellationToken);
                _ = PersistStreamEntry(segment, cancellationToken);
                await Task.WhenAll(distributeStreamTask, chatEntryTask);
            }
        }

        private async Task PersistStreamEntry(AudioRecordSegmentAccessor audioRecordSegmentAccessor, CancellationToken cancellationToken)
        {
            var audioEntry = await audioRecordSegmentAccessor.GetPartOnCompletion(cancellationToken);
            await _audioSaver.Save(audioEntry, cancellationToken);
        }

        private async Task TranscribeStreamEntry(AudioRecordSegmentAccessor audioRecordSegmentAccessor, CancellationToken cancellationToken)
        {
            var transcriptionResults = Transcribe(audioRecordSegmentAccessor, cancellationToken);
            await DistributeTranscriptionResults(audioRecordSegmentAccessor.StreamId, transcriptionResults, cancellationToken);
        }

        private async Task DistributeTranscriptionResults(
            StreamId streamId,
            IAsyncEnumerable<TranscriptPart> transcriptionResults,
            CancellationToken cancellationToken)
        {
            var channel = Channel.CreateBounded<TranscriptPart>(
                new BoundedChannelOptions(100) {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = true,
                    AllowSynchronousContinuations = true
                });

            var publishTask = _transcriptStreamer.PublishStream(streamId, channel.Reader, cancellationToken);

            _ = PushTranscriptionResults(channel.Writer, cancellationToken);

            await publishTask;

            async Task PushTranscriptionResults(ChannelWriter<TranscriptPart> writer, CancellationToken ct)
            {
                await foreach (var message in transcriptionResults.WithCancellation(ct))
                    await writer.WriteAsync(message, ct);

                writer.Complete();
            }
        }

        private async IAsyncEnumerable<TranscriptPart> Transcribe(AudioRecordSegmentAccessor audioRecordSegmentAccessor, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var r = audioRecordSegmentAccessor.AudioRecord;
            // TODO(AK): read actual config
            var command = new BeginTranscriptionCommand {
                RecordingId = r.Id.Value,
                AudioFormat = new AudioFormat {
                    Codec = AudioCodec.Opus,
                    ChannelCount = 1,
                    SampleRate = 48_000
                },
                Options = new TranscriptionOptions {
                    Language = "ru-RU",
                    IsDiarizationEnabled = false,
                    IsPunctuationEnabled = true,
                    MaxSpeakerCount = 1
                }
            };
            var transcriptId = await _transcriber.BeginTranscription(command, cancellationToken);

            var reader = audioRecordSegmentAccessor.GetStream();
            _ = PushAudioStreamForTranscription(transcriptId, reader, cancellationToken);

            var index = 0;
            var result = await _transcriber.PollTranscription(new PollTranscriptionCommand(transcriptId, index), cancellationToken);
            while (result.ContinuePolling && !cancellationToken.IsCancellationRequested) {
                foreach (var fragmentVariant in result.Fragments) {
                    if (fragmentVariant.Speech is { } speechFragment) {
                        var message = new TranscriptPart(
                            speechFragment.Text,
                            speechFragment.TextIndex,
                            speechFragment.StartOffset,
                            speechFragment.Duration);
                        yield return message;
                    }
                    else if (fragmentVariant.Error != null) {
                        // TODO(AK) - think about additional scenarios of transcription error handling
                    }

                    index = fragmentVariant.Value!.Index + 1;
                }

                result = await _transcriber.PollTranscription(
                    new PollTranscriptionCommand(transcriptId, index),
                    cancellationToken);
            }

            await _transcriber.AckTranscription(new AckTranscriptionCommand(transcriptId, index), cancellationToken);

            async Task PushAudioStreamForTranscription(Symbol tId, ChannelReader<BlobPart> r, CancellationToken ct)
            {
                await foreach (var (_, chunk) in r.ReadAllAsync(ct)) {
                    var appendCommand = new AppendTranscriptionCommand(tId, chunk);
                    await _transcriber.AppendTranscription(appendCommand, ct);
                }

                await _transcriber.EndTranscription(new EndTranscriptionCommand(tId), ct);
            }
        }

        private async Task PublishChatEntry(
            AudioRecordSegmentAccessor audioRecordSegmentAccessor,
            CancellationToken cancellationToken)
        {
            var e = audioRecordSegmentAccessor;
            var command = new ChatCommands.ServerPost(
                e.AudioRecord.UserId,
                e.AudioRecord.ChatId,
                "...",
                e.StreamId).MarkServerSide();
            await _chat.ServerPost(command, cancellationToken);
        }

        private Task DistributeAudioStream(AudioRecordSegmentAccessor audioRecordSegmentAccessor, CancellationToken cancellationToken)
            => _blobStreamer.PublishStream(audioRecordSegmentAccessor.StreamId, audioRecordSegmentAccessor.GetStream(), cancellationToken);
    }
}
