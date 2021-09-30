using System.Buffers;
using ActualChat.Audio;
using Microsoft.Extensions.Logging.Abstractions;
using Stl.Serialization;
using Stl.Testing;
using Stl.Text;
using Xunit.Abstractions;

namespace ActualChat.Transcription.IntegrationTests
{
    public class GoogleTranscriberTest : TestBase
    {
        public GoogleTranscriberTest(ITestOutputHelper @out) : base(@out) { }

        [Fact]
        public async Task BeginTranscriptionTest()
        {
            // TODO: Introduce DI Containers
            // TODO: Implement XUnitLogger
            var transcriber = new GoogleTranscriber(NullLogger<GoogleTranscriber>.Instance);
            var command = new BeginTranscriptionCommand {
                RecordId = Ulid.NewUlid().ToString(),
                AudioFormat = new AudioFormat {
                    Codec = AudioCodec.Opus,
                    ChannelCount = 1,
                    SampleRate = 16_000
                },
                Options = new TranscriptionOptions {
                    Language = "ru-RU",
                    IsDiarizationEnabled = false,
                    IsPunctuationEnabled = true,
                    MaxSpeakerCount = 1
                }
            };
            var transcriptId = await transcriber.BeginTranscription(command, default);

            transcriptId.Should().NotBeNull();
            transcriptId.Value.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public async Task BasicTranscriptionTest()
        {
            // TODO: Introduce DI Containers
            // TODO: Implement XUnitLogger
            var transcriber = new GoogleTranscriber(NullLogger<GoogleTranscriber>.Instance);
            var command = new BeginTranscriptionCommand {
                RecordId = Ulid.NewUlid().ToString(),
                AudioFormat = new AudioFormat {
                    Codec = AudioCodec.Opus,
                    ChannelCount = 1,
                    SampleRate = 48000
                },
                Options = new TranscriptionOptions {
                    Language = "ru-RU",
                    IsDiarizationEnabled = false,
                    IsPunctuationEnabled = true,
                    MaxSpeakerCount = 1
                }
            };
            var transcriptId = await transcriber.BeginTranscription(command, default);
            var feedTask = FeedTranscriber(transcriptId, transcriber, "file.webm");
            var cts = new CancellationTokenSource();
            var pollTask = PollResults(transcriptId, transcriber, cts.Token);

            await feedTask;

            await transcriber.EndTranscription(new EndTranscriptionCommand(transcriptId), default);

            await pollTask;

        }

        [Fact]
        public async Task PausesTranscriptionTest()
        {
            // TODO: Introduce DI Containers
            // TODO: Implement XUnitLogger
            var transcriber = new GoogleTranscriber(NullLogger<GoogleTranscriber>.Instance);
            var command = new BeginTranscriptionCommand {
                RecordId = Ulid.NewUlid().ToString(),
                AudioFormat = new AudioFormat {
                    Codec = AudioCodec.Opus,
                    ChannelCount = 1,
                    SampleRate = 48000
                },
                Options = new TranscriptionOptions {
                    Language = "ru-RU",
                    IsDiarizationEnabled = false,
                    IsPunctuationEnabled = true,
                    MaxSpeakerCount = 1
                }
            };
            var transcriptId = await transcriber.BeginTranscription(command, default);
            var feedTask = FeedTranscriber(transcriptId, transcriber, "pauses.webm");
            var cts = new CancellationTokenSource();
            var pollTask = PollResults(transcriptId, transcriber, cts.Token);

            await feedTask;

            await transcriber.EndTranscription(new EndTranscriptionCommand(transcriptId), default);

            await pollTask;
        }

        private async Task FeedTranscriber(Symbol transcriptId, ITranscriber t, string file)
        {
            await foreach (var data in ReadAudioFileSimulatingSpeech(file))
                await t.AppendTranscription(new AppendTranscriptionCommand {
                    TranscriptId = transcriptId,
                    Data = data
                }, default);
            await Task.Delay(300); // additional delay, google doesn't return final results otherwise.
        }


        private async Task PollResults(Symbol transcriptId, ITranscriber t, CancellationToken cancellationToken)
        {
            var index = 0;
            while (true) {
                var command = new PollTranscriptionCommand(transcriptId, index);
                var result = await t.PollTranscription(command, cancellationToken);
                if (!result.ContinuePolling)
                    break;

                Out.WriteLine("Result:");
                foreach (var fragmentVariant in result.Fragments) {
                    if (index < fragmentVariant.Value!.Index)
                        index = fragmentVariant.Value!.Index;

                    Out.WriteLine(fragmentVariant.ToString());
                    if (fragmentVariant.Speech is { IsFinal: true })
                        break;
                }

                index++;
            }
        }

        private async IAsyncEnumerable<Base64Encoded> ReadAudioFileSimulatingSpeech(string file)
        {
            await using var inputStream = new FileStream(Path.Combine(Environment.CurrentDirectory, "data", file), FileMode.Open, FileAccess.Read);
            using var bufferLease = MemoryPool<byte>.Shared.Rent(3 * 1024);
            var buffer = bufferLease.Memory;
            var bytesRead = await inputStream.ReadAsync(buffer);
            while (bytesRead > 0) {
                await Task.Delay(320);

                yield return new Base64Encoded(buffer[..bytesRead].ToArray());
                bytesRead = await inputStream.ReadAsync(buffer);
            }
        }
    }
}
