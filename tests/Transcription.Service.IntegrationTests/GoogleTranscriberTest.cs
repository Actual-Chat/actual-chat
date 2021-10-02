using System.Buffers;
using System.Threading.Channels;
using ActualChat.Audio;
using ActualChat.Blobs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Stl.Serialization;
using Stl.Testing;
using Stl.Text;
using Xunit.Abstractions;

namespace ActualChat.Transcription.IntegrationTests
{
    public class GoogleTranscriberTest : TestBase
    {
        private readonly ILogger<GoogleTranscriber> _logger;
        public GoogleTranscriberTest(ITestOutputHelper @out, ILogger<GoogleTranscriber> logger) : base(@out)
        {
            _logger = logger;
        }

        [Theory]
        [InlineData("file.webm")]
        [InlineData("pauses.webm")]
        public async Task TranscribeTest(string fileName)
        {
            var transcriber = new GoogleTranscriber(_logger);
            var request = new TranscriptionRequest(
                "123",
                new AudioFormat { Codec = AudioCodec.Opus, ChannelCount = 1, SampleRate = 48_000 },
                new TranscriptionOptions {
                    Language = "ru-RU",
                    IsDiarizationEnabled = false,
                    IsPunctuationEnabled = true,
                    MaxSpeakerCount = 1
                });
            var channel = Channel.CreateUnbounded<BlobPart>(new UnboundedChannelOptions
                { SingleReader = true, SingleWriter = true });

            _ = ReadAudioFileSimulatingSpeech(fileName, channel.Writer);

            var transcriptResult = await transcriber.Transcribe(request, channel.Reader, CancellationToken.None);
            await foreach (var speechFragment in transcriptResult.ReadAllAsync())
                _logger.LogInformation(speechFragment.Text);
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

        private async Task ReadAudioFileSimulatingSpeech(string file, ChannelWriter<BlobPart> writer)
        {
            var index = 0;
            Exception? error = null;
            try {
                await foreach (var base64Encoded in ReadAudioFileSimulatingSpeech(file))
                    writer.TryWrite(new BlobPart(index++, base64Encoded.Data));
            }
            catch (Exception e) {
                error = e;
            }
            finally {
                writer.Complete(error);
            }
        }
    }
}
