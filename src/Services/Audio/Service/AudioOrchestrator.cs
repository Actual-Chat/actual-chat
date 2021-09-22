using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ActualChat.Audio.Orchestration;
using ActualChat.Blobs;
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
        private readonly AudioRecordProducer _audioRecorder;
        private readonly AudioActivityExtractor _audioActivityExtractor;
        private readonly AudioStreamPublisher _audioPublisher;
        private readonly TranscriptStreamPublisher _transcriptPublisher;
        private readonly IServerSideChatService _chat;
        private readonly ILogger<AudioOrchestrator> _log;

        public static bool SkipAutoStart { get; set; } = true;

        public AudioOrchestrator(
            ITranscriber transcriber,
            AudioSaver audioSaver,
            AudioRecordProducer audioRecorder,
            AudioActivityExtractor audioActivityExtractor,
            AudioStreamPublisher audioPublisher,
            TranscriptStreamPublisher transcriptPublisher,
            IServerSideChatService chat,
            ILogger<AudioOrchestrator> log)
        {
            _transcriber = transcriber;
            _audioSaver = audioSaver;
            _audioRecorder = audioRecorder;
            _audioActivityExtractor = audioActivityExtractor;
            _audioPublisher = audioPublisher;
            _transcriptPublisher = transcriptPublisher;
            _chat = chat;
            _log = log;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (SkipAutoStart)
                return;

            // TODO(AK): add push-back based on current node performance metrics \ or provide signals for scale-out
            while (true) {
                var record = await FetchNewAudioRecord(stoppingToken);
                _ = StartAudioPipeline(record!, stoppingToken);
            }
        }

        internal ValueTask<AudioRecord> FetchNewAudioRecord(CancellationToken cancellationToken)
            => _audioRecorder.Produce(cancellationToken);

        internal async Task StartAudioPipeline(AudioRecord audioRecord, CancellationToken cancellationToken)
        {
            var audioReader = await _audioRecorder.GetStream(audioRecord.Id, cancellationToken);
            var segments = _audioActivityExtractor.GetSegmentsWithAudioActivity(audioRecord, audioReader, cancellationToken);
            while (await segments.WaitToReadAsync(cancellationToken))
            while (segments.TryRead(out var segment)) {
                var distributeStreamTask = DistributeAudioStream(segment, cancellationToken);
                var chatEntryTask = PublishChatEntry(segment, cancellationToken);
                _ = TranscribeSegment(segment, cancellationToken);
                _ = PersistSegment(segment, cancellationToken);
                await Task.WhenAll(distributeStreamTask, chatEntryTask);
            }
        }

        private async Task PersistSegment(AudioRecordSegment audioRecordSegment, CancellationToken cancellationToken)
        {
            await audioRecordSegment.Run();
            var audioStreamPart = audioRecordSegment.AudioStreamPart;
            await _audioSaver.Save(audioStreamPart, cancellationToken);
        }

        private async Task TranscribeSegment(AudioRecordSegment audioRecordSegment, CancellationToken cancellationToken)
        {
            var transcriptionResults = Transcribe(audioRecordSegment, cancellationToken);
            await PublishTranscriptionResults(audioRecordSegment.StreamId, transcriptionResults, cancellationToken);
        }

        private async Task PublishTranscriptionResults(
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

            var publishTask = _transcriptPublisher.PublishStream(streamId, channel.Reader, cancellationToken);
            _ = PushTranscriptionResults(channel.Writer, cancellationToken);
            await publishTask;

            async Task PushTranscriptionResults(ChannelWriter<TranscriptPart> writer, CancellationToken ct)
            {
                await foreach (var message in transcriptionResults.WithCancellation(ct))
                    await writer.WriteAsync(message, ct);

                writer.Complete();
            }
        }

        private async IAsyncEnumerable<TranscriptPart> Transcribe(
            AudioRecordSegment audioRecordSegment,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var r = audioRecordSegment.AudioRecord;
            // TODO(AK): read actual config
            var command = new BeginTranscriptionCommand {
                RecordId = (string) r.Id,
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

            var reader = audioRecordSegment.GetStream();
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
                await foreach (var (_, data) in r.ReadAllAsync(ct)) {
                    var appendCommand = new AppendTranscriptionCommand(tId, data);
                    await _transcriber.AppendTranscription(appendCommand, ct);
                }

                await _transcriber.EndTranscription(new EndTranscriptionCommand(tId), ct);
            }
        }

        private async Task PublishChatEntry(
            AudioRecordSegment audioRecordSegment,
            CancellationToken cancellationToken)
        {
            var e = audioRecordSegment;
            var chatEntry = new ChatEntry(e.AudioRecord.ChatId, 0) {
                AuthorId = e.AudioRecord.UserId,
                Content = "...",
                ContentType = ChatContentType.Text,
                StreamId = e.StreamId
            };
            await _chat.CreateEntry( new ChatCommands.CreateEntry(chatEntry).MarkServerSide(), cancellationToken);
        }

        private Task DistributeAudioStream(AudioRecordSegment audioRecordSegment, CancellationToken cancellationToken)
            => _audioPublisher.PublishStream(audioRecordSegment.StreamId, audioRecordSegment.GetStream(), cancellationToken);
    }
}
