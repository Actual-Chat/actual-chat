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
        private readonly AudioPersistService _audioPersistService;
        private readonly IServerSideRecordingService<AudioRecording> _recordingService;
        private readonly IServerSideStreamingService<BlobMessage> _streamingService;
        private readonly IServerSideStreamingService<TranscriptMessage> _transcriptStreamingService;
        private readonly IServerSideChatService _chatService;
        private readonly ILogger<AudioOrchestrator> _log;

        public static bool SkipAutoStart { get; set; } = true;

        public AudioOrchestrator(
            ITranscriber transcriber,
            AudioPersistService audioPersistService,
            IServerSideRecordingService<AudioRecording> recordingService,
            IServerSideStreamingService<BlobMessage> streamingService,
            IServerSideStreamingService<TranscriptMessage> transcriptStreamingService,
            IServerSideChatService chatService,
            ILogger<AudioOrchestrator> log)
        {
            _transcriber = transcriber;
            _audioPersistService = audioPersistService;
            _recordingService = recordingService;
            _streamingService = streamingService;
            _transcriptStreamingService = transcriptStreamingService;
            _chatService = chatService;
            _log = log;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if(SkipAutoStart)
                return;
            
            while (true) {
                // TODO(AK): add push-back based on current node performance metrics \ or provide signals for scale-out 
                var recording = await WaitForNewRecording(stoppingToken);
                if (stoppingToken.IsCancellationRequested)
                    return;

                _ = StartAudioPipeline(recording!, stoppingToken);
            }
        }

        internal async Task<AudioRecording?> WaitForNewRecording(CancellationToken cancellationToken)
        {
            var recording = await _recordingService.WaitForNewRecording(cancellationToken);
            while (recording == null && !cancellationToken.IsCancellationRequested) 
                recording = await _recordingService.WaitForNewRecording(cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            return recording;
        }

        internal async Task StartAudioPipeline(AudioRecording recording, CancellationToken cancellationToken)
        {
            var audioReader = await _recordingService.GetRecording(recording.Id, cancellationToken);
            await foreach (var audioStreamEntry in SplitStreamBySilencePeriods(recording, audioReader, cancellationToken)) {
                var distributeStreamTask = DistributeAudioStream(audioStreamEntry, cancellationToken);
                var chatEntryTask = PublishChatEntry(audioStreamEntry, cancellationToken);

                _ = TranscribeStreamEntry(audioStreamEntry, cancellationToken);

                _ = PersistStreamEntry(audioStreamEntry, cancellationToken);

                await Task.WhenAll(distributeStreamTask, chatEntryTask);
            }

        }

        private async Task PersistStreamEntry(AudioStreamEntry audioStreamEntry, CancellationToken cancellationToken)
        {
            var audioEntry = await audioStreamEntry.GetEntryOnCompletion(cancellationToken);
            await _audioPersistService.Persist(audioEntry, cancellationToken);
        }

        private async Task TranscribeStreamEntry(AudioStreamEntry audioStreamEntry, CancellationToken cancellationToken)
        {
            var transcriptionResults = Transcribe(audioStreamEntry, cancellationToken);
            await DistributeTranscriptionResults(audioStreamEntry.StreamId, transcriptionResults, cancellationToken);
        }

        private async Task DistributeTranscriptionResults(
            StreamId streamId,
            IAsyncEnumerable<TranscriptMessage> transcriptionResults,
            CancellationToken cancellationToken)
        {
            var channel = Channel.CreateBounded<TranscriptMessage>(
                new BoundedChannelOptions(100) {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = true,
                    AllowSynchronousContinuations = true
                });

            var publishTask = _transcriptStreamingService.PublishStream(streamId, channel.Reader, cancellationToken);

            _ = PushTranscriptionResults(channel.Writer, cancellationToken);

            await publishTask;
            
            async Task PushTranscriptionResults(ChannelWriter<TranscriptMessage> writer, CancellationToken ct)
            {
                await foreach (var message in transcriptionResults.WithCancellation(ct))
                    await writer.WriteAsync(message, ct);
                
                writer.Complete();
            }
        }

        private async IAsyncEnumerable<TranscriptMessage> Transcribe(AudioStreamEntry audioStreamEntry, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var (recordingId, _, _, configuration) = audioStreamEntry.AudioRecording;
            // TODO(AK): read actual config
            var command = new BeginTranscriptionCommand {
                RecordingId = recordingId.Value,
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

            var reader = audioStreamEntry.GetStream();
            _ = PushAudioStreamForTranscription(transcriptId, reader, cancellationToken);

            var index = 0;
            var result = await _transcriber.PollTranscription(new PollTranscriptionCommand(transcriptId, index), cancellationToken);
            while (result.ContinuePolling && !cancellationToken.IsCancellationRequested) {
                foreach (var fragmentVariant in result.Fragments) {
                    if (fragmentVariant.Speech is { } speechFragment) {
                        var message = new TranscriptMessage(
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

            async Task PushAudioStreamForTranscription(Symbol tId, ChannelReader<BlobMessage> r, CancellationToken ct)
            {
                await foreach (var (_, chunk) in r.ReadAllAsync(ct)) {
                    var appendCommand = new AppendTranscriptionCommand(tId, chunk);
                    await _transcriber.AppendTranscription(appendCommand, ct);
                }

                await _transcriber.EndTranscription(new EndTranscriptionCommand(tId), ct);
            }
        }

        private async Task PublishChatEntry(
            AudioStreamEntry audioStreamEntry,
            CancellationToken cancellationToken)
        {
            var (streamId, _, (_, userId, chatId, _)) = audioStreamEntry;
            var command = new ChatCommands.ServerPost(
                userId,
                chatId,
                "...",
                streamId).MarkServerSide();
            await _chatService.ServerPost(command, cancellationToken);
        }

        private Task DistributeAudioStream(AudioStreamEntry audioStreamEntry, CancellationToken cancellationToken) 
            => _streamingService.PublishStream(audioStreamEntry.StreamId, audioStreamEntry.GetStream(), cancellationToken);

        private IAsyncEnumerable<AudioStreamEntry> SplitStreamBySilencePeriods(
            AudioRecording audioRecording,
            ChannelReader<BlobMessage> audioReader,
            CancellationToken cancellationToken)
        {
            var splitter = new AudioStreamSplitter();
            return splitter.SplitBySilencePeriods(audioRecording, audioReader, cancellationToken);
        }
    }
}