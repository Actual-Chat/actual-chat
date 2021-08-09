using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using ActualChat.Audio;
using Google.Api.Gax.Grpc;
using Google.Cloud.Speech.V1;
using Google.Protobuf;
using Stl.Fusion.Extensions;
using Stl.Text;

namespace ActualChat.Transcription
{
    public class GoogleTranscriber : ITranscriber
    {
        // private SpeechClient.StreamingRecognizeStream _streamingRecognizeStream;
        private readonly ConcurrentDictionary<string,TranscriptionStream> _transcriptionStreams 
            = new ConcurrentDictionary<string, TranscriptionStream>();

        public async Task<Symbol> BeginTranscription(BeginTranscriptionCommand command, CancellationToken cancellationToken = default)
        {
            var builder = new SpeechClientBuilder();
            var speechClient = await builder.BuildAsync(cancellationToken);
            var config = new RecognitionConfig {
                Encoding = MapEncoding(command.AudioFormat.Codec),
                AudioChannelCount = command.AudioFormat.ChannelCount,
                SampleRateHertz = command.AudioFormat.SampleRate,
                LanguageCode = command.Options.Language,
                EnableAutomaticPunctuation = command.Options.IsPunctuationEnabled,
                DiarizationConfig = new SpeakerDiarizationConfig {
                    EnableSpeakerDiarization = command.Options.IsDiarizationEnabled,
                    MaxSpeakerCount = command.Options.MaxSpeakerCount ?? 5
                }
            };
            var streamingRecognizeStream = speechClient.StreamingRecognize();
            var transcriptId = Ulid.NewUlid().ToString();
            _transcriptionStreams.TryAdd(transcriptId,
                new TranscriptionStream(streamingRecognizeStream, streamingRecognizeStream.GetResponseStream(), config));
            
            return transcriptId;
        }

        public async Task AppendTranscription(AppendTranscriptionCommand command, CancellationToken cancellationToken = default)
        {
            var (transcriptId, data) = command;
            
            // Waiting for BeginTranscription
            var waitAttempts = 0;
            while (!_transcriptionStreams.ContainsKey(transcriptId) && waitAttempts < 5) {
                await Task.Delay(10, cancellationToken);
                waitAttempts++;
            }
            
            // Initialize hasn't been completed or Recording has already been completed
            if (!_transcriptionStreams.TryGetValue(transcriptId, out var transcriptionStream)) return;

            var (writer, _, config) = transcriptionStream;
            await writer.WriteAsync(new StreamingRecognizeRequest {
                StreamingConfig = new StreamingRecognitionConfig {
                    Config = config,
                    InterimResults = false,
                    SingleUtterance = false
                },
                AudioContent = ByteString.CopyFrom(data.Data)
            });
        }

        public async Task EndTranscription(EndTranscriptionCommand command, CancellationToken cancellationToken = default)
        {
            if (_transcriptionStreams.TryRemove(command.TranscriptId, out var tuple)) {
                var (writer, _, _) = tuple;
                await writer.WriteCompleteAsync();
            }
        }

        public Task<Transcript> GetTranscript(Symbol transcriptId, CancellationToken cancellationToken = default) => throw new System.NotImplementedException();

        public Task<TranscriptSummary> GetSummary(Symbol transcriptId, CancellationToken cancellationToken = default) => throw new System.NotImplementedException();

        public Task<TranscriptAudioSummary> GetAudioSummary(Symbol transcriptId, CancellationToken cancellationToken = default) => throw new System.NotImplementedException();

        public Task<ImmutableArray<TranscriptFragment>> GetFragments(Symbol transcriptId, PageRef<int> page, CancellationToken cancellationToken = default) => throw new System.NotImplementedException();

        
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
        
        private record TranscriptionStream(SpeechClient.StreamingRecognizeStream Writer, AsyncResponseStream<StreamingRecognizeResponse> Reader, RecognitionConfig Config);
    }
}