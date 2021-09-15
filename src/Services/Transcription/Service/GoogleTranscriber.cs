using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ActualChat.Audio;
using Google.Cloud.Speech.V1P1Beta1;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Stl.Async;
using Stl.Text;

namespace ActualChat.Transcription
{
    public class GoogleTranscriber : ITranscriber
    {
        private readonly ILogger<GoogleTranscriber> _log;
        private readonly ConcurrentDictionary<string,TranscriptionStream> _transcriptionStreams = new();

        public GoogleTranscriber(ILogger<GoogleTranscriber> log)
        {
            _log = log;
        }

        public async Task<Symbol> BeginTranscription(BeginTranscriptionCommand command, CancellationToken cancellationToken = default)
        {
            var (recordingId, options, format) = command;
            _log.LogInformation($"{nameof(BeginTranscription)}, RecordingId = {{RecordingId}}", recordingId);

            var builder = new SpeechClientBuilder();
            var speechClient = await builder.BuildAsync(cancellationToken);
            var config = new RecognitionConfig {
                Encoding = MapEncoding(format.Codec),
                AudioChannelCount = format.ChannelCount,
                SampleRateHertz = format.SampleRate,
                LanguageCode = options.Language,
                EnableAutomaticPunctuation = options.IsPunctuationEnabled,
                DiarizationConfig = new SpeakerDiarizationConfig {
                    EnableSpeakerDiarization = true,//options.IsDiarizationEnabled,
                    MaxSpeakerCount = options.MaxSpeakerCount ?? 5
                }
            };
            var streamingRecognizeStream = speechClient.StreamingRecognize();
            await streamingRecognizeStream.WriteAsync(new StreamingRecognizeRequest {
                StreamingConfig = new StreamingRecognitionConfig {
                    Config = config,
                    InterimResults = true,
                    SingleUtterance = false
                }
            });
            var transcriptId = $"{recordingId}-{Ulid.NewUlid().ToString()}";
            var responseStream =  streamingRecognizeStream.GetResponseStream();
            var reader = new TranscriptionBuffer(responseStream, _log);
            var cts = new CancellationTokenSource();
            var transcriptProcessor = reader.Start(cts.Token);
            _transcriptionStreams.TryAdd(transcriptId,
                new TranscriptionStream(streamingRecognizeStream, reader, transcriptProcessor, cts));

            return transcriptId;
        }

        public async Task AppendTranscription(AppendTranscriptionCommand command, CancellationToken cancellationToken = default)
        {
            var (transcriptId, data) = command;
            _log.LogInformation($"{nameof(AppendTranscription)}, TranscriptId = {{TranscriptId}}", transcriptId);

            // Waiting for BeginTranscription
            var waitAttempts = 0;
            while (!_transcriptionStreams.ContainsKey(transcriptId) && waitAttempts < 5) {
                await Task.Delay(10, cancellationToken);
                waitAttempts++;
            }

            // Initialize hasn't been completed or Recording has already been completed
            if (!_transcriptionStreams.TryGetValue(transcriptId, out var transcriptionStream)) return;

            await transcriptionStream.Writer.WriteAsync(new StreamingRecognizeRequest {
                AudioContent = ByteString.CopyFrom(data.Data)
            });
        }

        public async Task EndTranscription(EndTranscriptionCommand command, CancellationToken cancellationToken = default)
        {
            _log.LogInformation($"{nameof(EndTranscription)}, TranscriptId = {{TranscriptId}}", command.TranscriptId);
            
            if (_transcriptionStreams.TryGetValue(command.TranscriptId, out var tuple)) {
                var (writer, _, transcriptProcessorTask, _) = tuple;
                await writer.WriteCompleteAsync();
                await transcriptProcessorTask;

                _transcriptionStreams.TryRemove(command.TranscriptId, out _);
            }
        }

        public async Task<PollResult> PollTranscription(PollTranscriptionCommand command, CancellationToken cancellationToken = default)
        {
            var (transcriptId, index) = command;
            _log.LogInformation($"{nameof(PollTranscription)}, TranscriptId = {{TranscriptId}}, Index = {{Index}}", transcriptId, index);

            if (!_transcriptionStreams.TryGetValue(transcriptId, out var transcriptionStream))
                return new PollResult(false, ImmutableArray<TranscriptFragmentVariant>.Empty);

            var fragments = await transcriptionStream.Reader.GetResults(index, cancellationToken);
            return new PollResult(!cancellationToken.IsCancellationRequested, fragments);
        }

        public Task AckTranscription(AckTranscriptionCommand command, CancellationToken cancellationToken = default)
        {
            var (transcriptId, index) = command;
            _log.LogInformation($"{nameof(AckTranscription)}, TranscriptId = {{TranscriptId}}, Index = {{Index}}", transcriptId, index);

            if (!_transcriptionStreams.TryGetValue(transcriptId, out var transcriptionStream))
                return Task.CompletedTask;

            transcriptionStream.Reader.FreeBuffer(index);
            return Task.CompletedTask;
        }

        private static RecognitionConfig.Types.AudioEncoding MapEncoding(AudioCodec codec)
        {
            switch (codec) {
                case AudioCodec.Wav:
                    return RecognitionConfig.Types.AudioEncoding.Linear16;
                case AudioCodec.Flac:
                    return RecognitionConfig.Types.AudioEncoding.Flac;
                case AudioCodec.Opus:
                    return RecognitionConfig.Types.AudioEncoding.WebmOpus;
                default:
                    return RecognitionConfig.Types.AudioEncoding.EncodingUnspecified;
            }
        }

        private record TranscriptionStream(
            SpeechClient.StreamingRecognizeStream Writer,
            TranscriptionBuffer Reader,
            Task TranscriptProcessor,
            CancellationTokenSource CancellationTokenSource);

        private class TranscriptionBuffer
        {
            private const int PollWaitDelay = 10_000;
            
            private readonly IAsyncEnumerable<StreamingRecognizeResponse> _stream;
            private readonly ILogger<GoogleTranscriber> _logger;

            private readonly Channel<TranscriptFragmentVariant> _channel =
                Channel.CreateUnbounded<TranscriptFragmentVariant>(new UnboundedChannelOptions{ SingleWriter = true });
            private readonly LinkedList<TranscriptFragmentVariant> _bufferedResults = new();
            private readonly object _lock = new();

            private int _index;
            private double _offset;
            private int _startCalled;

            public TranscriptionBuffer(IAsyncEnumerable<StreamingRecognizeResponse> stream, ILogger<GoogleTranscriber> logger)
            {
                _stream = stream;
                _logger = logger;
            }


            public async Task Start(CancellationToken cancellationToken = default)
            {
                if (Interlocked.CompareExchange(ref _startCalled, 1, 0) != 0) 
                    return;

                var cutter = new StablePrefixCutter();
                await foreach (var response in _stream.WithCancellation(cancellationToken)) {
                    var fragmentVariants = MapResponse(response);
                    foreach (var fragmentVariant in fragmentVariants) {
                        var speechFragment = fragmentVariant.Speech;
                        if ((speechFragment?.StartOffset ?? 0) != 0)
                            await _channel.Writer.WriteAsync(fragmentVariant, cancellationToken);
                        else if (speechFragment != null) {
                            var processedFragment = cutter.CutMemoized(speechFragment);
                            await _channel.Writer.WriteAsync(new TranscriptFragmentVariant(processedFragment), cancellationToken);
                        }
                        else
                            await _channel.Writer.WriteAsync(fragmentVariant, cancellationToken);
                    }
                }

                _channel.Writer.Complete();
            }

            public async Task<ImmutableArray<TranscriptFragmentVariant>> GetResults(int startingIndex, CancellationToken cancellationToken)
            {
                lock (_lock) {
                    var result = _bufferedResults
                        .SkipWhile(r => r.Value!.Index < startingIndex)
                        .ToImmutableArray();
                    if (result.Length > 0)
                        return result;
                }

                var readWait = _channel.Reader.WaitToReadAsync(cancellationToken).AsTask();
                var delayWait = Task.Delay(PollWaitDelay, cancellationToken);
                await Task.WhenAny(readWait, delayWait);
                if (readWait.Status != TaskStatus.RanToCompletion)
                    return ImmutableArray<TranscriptFragmentVariant>.Empty;
                
                var freshResults = new List<TranscriptFragmentVariant>();
                lock (_lock)
                    while (_channel.Reader.TryRead(out var item)) {
                        freshResults.Add(item);
                        _bufferedResults.AddLast(item);
                    }

                return freshResults.ToImmutableArray();

            }

            public void FreeBuffer(int beforeIndex)
            {
                lock (_lock) {
                    if (_bufferedResults.Count == 0)
                        return;

                    if (_bufferedResults.First!.Value.Value!.Index > beforeIndex)
                        return;

                    while (_bufferedResults.First!.Value.Value!.Index <= beforeIndex) 
                        _bufferedResults.RemoveFirst();
                }
            }

            private IReadOnlyList<TranscriptFragmentVariant> MapResponse(StreamingRecognizeResponse response)
            {
                if (response.Error != null) {
                    _logger.LogError("Transcription error: Code {ErrorCode}; Message: {ErrorMessage}", response.Error.Code, response.Error.Message);
                    return new [] { new TranscriptFragmentVariant(
                        new TranscriptErrorFragment(response.Error.Code, response.Error.Message) {
                            Index = _index++,
                            StartOffset = _offset,
                            Duration = 0d
                        })};
                }
                foreach (var result in response.Results) {
                    var alternative = result.Alternatives.First();
                    var endTime = result.ResultEndTime;
                    var endOffset = endTime.ToTimeSpan().TotalSeconds;
                    var fragment = new TranscriptSpeechFragment {
                        Index = _index++,
                        Confidence = alternative.Confidence,
                        Text = alternative.Transcript,
                        StartOffset = 0,
                        Duration = endOffset,
                        IsFinal = false
                    };
                    _offset = endOffset;
                    if (!result.IsFinal || !(alternative.Words?.Count > 0))
                        return new[] { new TranscriptFragmentVariant(fragment) };
                    
                    var list = new List<TranscriptFragmentVariant>();
                    foreach (var wordInfo in alternative.Words) {
                        var startOffset = wordInfo.StartTime.ToTimeSpan().TotalSeconds;
                        var wordFragment = new TranscriptSpeechFragment {
                            Index = _index++,
                            Confidence = wordInfo.Confidence,
                            Text = wordInfo.Word,
                            StartOffset = startOffset,
                            Duration = Math.Round(wordInfo.EndTime.ToTimeSpan().TotalSeconds - startOffset, 3, MidpointRounding.AwayFromZero),
                            IsFinal = true,
                            SpeakerId = wordInfo.SpeakerTag.ToString()
                        };
                        list.Add(new TranscriptFragmentVariant(wordFragment));
                    }

                    return list;
                }

                return ImmutableArray<TranscriptFragmentVariant>.Empty;
            }
        }
    }
}
