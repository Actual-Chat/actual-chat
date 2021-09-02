using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ActualChat.Audio.Orchestration;
using ActualChat.Blobs;
using ActualChat.Distribution;
using ActualChat.Transcription;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Stl.Fusion.Authentication;

namespace ActualChat.Audio
{
    public class AudioOrchestrator : BackgroundService
    {
        private readonly IAuthService _authService;
        private readonly IBlobStorageProvider _blobStorageProvider;
        private readonly ITranscriber _transcriber;
        private readonly IServerSideAudioStreamingService _streamingService;
        private readonly IServerSideStreamingService<TranscriptMessage> _transcriptStreamingService;
        private readonly ILogger<AudioOrchestrator> _log;

        public AudioOrchestrator(
            IAuthService authService,
            IBlobStorageProvider blobStorageProvider,
            ITranscriber transcriber,
            IServerSideAudioStreamingService streamingService,
            IServerSideStreamingService<TranscriptMessage> transcriptStreamingService,
            ILogger<AudioOrchestrator> log)
        {
            _authService = authService;
            _blobStorageProvider = blobStorageProvider;
            _transcriber = transcriber;
            _streamingService = streamingService;
            _transcriptStreamingService = transcriptStreamingService;
            _log = log;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (true) {
                // TODO(AK): add push-back based on current node performance metrics \ or provide signals for scale-out 
                var recording = await WaitForNewRecording(stoppingToken);
                if (stoppingToken.IsCancellationRequested)
                    return;

                _ = StartAudioPipeline(recording!, stoppingToken);
            }
        }

        private async Task<AudioRecording?> WaitForNewRecording(CancellationToken cancellationToken)
        {
            var recording = await _streamingService.WaitForNewRecording(cancellationToken);
            while (recording == null && !cancellationToken.IsCancellationRequested) 
                recording = await _streamingService.WaitForNewRecording(cancellationToken);
            
            return recording;
        }

        private async Task StartAudioPipeline(AudioRecording recording, CancellationToken cancellationToken)
        {
            var audioReader = await _streamingService.GetStream(recording.Id, cancellationToken);
            await foreach (var audioStreamEntry in SplitStreamBySilencePeriods(recording, audioReader, cancellationToken)) {
                var distributeStreamTask = DistributeAudioStream(audioStreamEntry, cancellationToken);
                var chatEntryTask = PublishChatEntry(audioStreamEntry, cancellationToken);

                var transcriptionResults = await Transcribe(audioStreamEntry, cancellationToken);
                _ = DistributeTranscriptionResults(transcriptionResults, cancellationToken);

                _ = PersistStreamEntry(audioStreamEntry, cancellationToken);

                await Task.WhenAll(distributeStreamTask, chatEntryTask);
            }

        }

        private Task PersistStreamEntry(AudioStreamEntry audioStreamEntry, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        private Task DistributeTranscriptionResults(
            IAsyncEnumerable<TranscriptFragmentVariant> transcriptionResults,
            CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        private async Task<IAsyncEnumerable<TranscriptFragmentVariant>> Transcribe(AudioStreamEntry audioStreamEntry, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        private Task PublishChatEntry(AudioStreamEntry audioStreamEntry, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        private Task DistributeAudioStream(AudioStreamEntry audioStreamEntry, CancellationToken cancellationToken) 
            => _streamingService.PublishStream(audioStreamEntry.StreamId, audioStreamEntry.GetStream(), cancellationToken);

        private IAsyncEnumerable<AudioStreamEntry> SplitStreamBySilencePeriods(
            AudioRecording audioRecording,
            ChannelReader<AudioMessage> audioReader,
            CancellationToken cancellationToken)
        {
            var splitter = new AudioStreamSplitter();
            return splitter.SplitBySilencePeriods(audioRecording, audioReader, cancellationToken);
        }
    }
}