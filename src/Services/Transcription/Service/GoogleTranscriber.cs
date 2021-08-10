using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ActualChat.Audio;
using Google.Cloud.Speech.V1;
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
                    EnableSpeakerDiarization = options.IsDiarizationEnabled,
                    MaxSpeakerCount = options.MaxSpeakerCount ?? 5
                }
            };
            var streamingRecognizeStream = speechClient.StreamingRecognize();
            await streamingRecognizeStream.WriteAsync(new StreamingRecognizeRequest {
                StreamingConfig = new StreamingRecognitionConfig {
                    Config = config,
                    InterimResults = false,
                    SingleUtterance = false
                }
            });
            var transcriptId = $"{recordingId}-{Ulid.NewUlid().ToString()}";
            var responseStream =  streamingRecognizeStream.GetResponseStream();
            var reader = new TranscriptionBuffer(responseStream);
            var cts = new CancellationTokenSource();
            // reader.Start(cts.Token).ContinueWith(t
                // => EndTranscription(new EndTranscriptionCommand(transcriptId), CancellationToken.None).Ignore(), CancellationToken.None).Ignore();
            
            _transcriptionStreams.TryAdd(transcriptId,
                new TranscriptionStream(streamingRecognizeStream, reader, cts));
            
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

            var (writer, reader, cts) = transcriptionStream;
            await writer.WriteAsync(new StreamingRecognizeRequest {
                AudioContent = ByteString.CopyFrom(data.Data)
            });
            
            reader.Start(cts.Token).ContinueWith(t
                => EndTranscription(new EndTranscriptionCommand(transcriptId), CancellationToken.None).Ignore(), CancellationToken.None).Ignore();
        }

        public async Task EndTranscription(EndTranscriptionCommand command, CancellationToken cancellationToken = default)
        {
            _log.LogInformation($"{nameof(EndTranscription)}, TranscriptId = {{TranscriptId}}", command.TranscriptId);
            
            if (_transcriptionStreams.TryRemove(command.TranscriptId, out var tuple)) {
                var (writer, _, _) = tuple;
                await writer.WriteCompleteAsync();
            }
        }

        public Task<ImmutableArray<TranscriptFragmentVariant>> PollTranscription(PollTranscriptionCommand command, CancellationToken cancellationToken = default)
        {
            var (transcriptId, index) = command;
            _log.LogInformation($"{nameof(PollTranscription)}, TranscriptId = {{TranscriptId}}, Index = {{Index}}", transcriptId, index);
            
            if (!_transcriptionStreams.TryGetValue(transcriptId, out var transcriptionStream)) 
                return Task.FromResult(ImmutableArray<TranscriptFragmentVariant>.Empty);

            return Task.FromResult(transcriptionStream.Reader.GetResults(index));
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
                    return RecognitionConfig.Types.AudioEncoding.OggOpus;
                default:
                    throw new ArgumentOutOfRangeException(nameof(codec), codec, null);
            }
        }
        
        private record TranscriptionStream(
            SpeechClient.StreamingRecognizeStream Writer, 
            TranscriptionBuffer Reader,
            CancellationTokenSource CancellationTokenSource);

        private class TranscriptionBuffer
        {
            private readonly IAsyncEnumerable<StreamingRecognizeResponse> _stream;
            private readonly ConcurrentQueue<TranscriptFragmentVariant> _bufferedResult = new();
            private readonly object _lock = new();

            private int _index;
            private double _offset;
            private int _startCalled;

            public TranscriptionBuffer(IAsyncEnumerable<StreamingRecognizeResponse> stream) => _stream = stream;


            public async Task Start(CancellationToken cancellationToken = default)
            {
                if (Interlocked.CompareExchange(ref _startCalled, 1, 0) != 0) return;
                
                await foreach (var response in _stream.WithCancellation(cancellationToken)) {
                    var fragmentVariant = MapResponse(response);
                    if (fragmentVariant != null) 
                        _bufferedResult.Enqueue(fragmentVariant);
                }
            }

            public ImmutableArray<TranscriptFragmentVariant> GetResults(int startingIndex)
            {
                return _bufferedResult
                    .SkipWhile(f => f.Value!.Index < startingIndex)
                    .ToImmutableArray();
            }

            public void FreeBuffer(int beforeIndex)
            {
                if (!_bufferedResult.TryPeek(out var fragment)) 
                    return;
                
                if (fragment.Value!.Index > beforeIndex) 
                    return;

                lock (_lock) {
                    if (!_bufferedResult.TryPeek(out fragment)) 
                        return;
                    
                    if (fragment.Value!.Index > beforeIndex) 
                        return;
                    
                    while (fragment.Value!.Index <= beforeIndex && _bufferedResult.TryDequeue(out fragment)) { }
                }
            }

            private TranscriptFragmentVariant? MapResponse(StreamingRecognizeResponse response)
            {
                if (response.Error != null) {
                    // Temporarily
                    throw new InvalidOperationException(response.Error.Message);
                    return null;
                }
                foreach (var result in response.Results.Where(r => r.IsFinal)) {
                    var alternative = result.Alternatives.First();
                    var endTime = result.ResultEndTime;
                    var endOffset = endTime.ToTimeSpan().TotalSeconds;
                    var fragment = new TranscriptSpeechFragment {
                        Index = _index++,
                        Confidence = alternative.Confidence,
                        Text = alternative.Transcript,
                        StartOffset = _offset,
                        Duration = endOffset - _offset
                    };
                    _offset = endOffset;
                    // one final result in the response
                    return new TranscriptFragmentVariant(fragment);
                }

                return null;
            }
        }
    }
}