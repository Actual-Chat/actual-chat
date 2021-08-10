using System;
using System.IO;
using System.Threading.Tasks;
using ActualChat.Audio;
using ActualChat.Transcription;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Stl.Serialization;
using Stl.Testing;
using Xunit;
using Xunit.Abstractions;

namespace ActualChat.Tests
{
    public class GoogleTranscriberTest : TestBase
    {
        public GoogleTranscriberTest(ITestOutputHelper @out) : base(@out)
        {
        }

        [Fact]
        public async Task BeginTranscriptionTest()
        {
            // TODO: Introduce DI Containers
            // TODO: Implement XUnitLogger
            var transcriber = new GoogleTranscriber(NullLogger<GoogleTranscriber>.Instance);
            var command = new BeginTranscriptionCommand {
                RecordingId = Ulid.NewUlid().ToString(),
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
            var transcriptId = await transcriber.BeginTranscription(command);

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
                RecordingId = Ulid.NewUlid().ToString(),
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
            var transcriptId = await transcriber.BeginTranscription(command);
            var audioBytes = await File.ReadAllBytesAsync(Path.Combine(Environment.CurrentDirectory, "data", "file.webm"));



            for (int i = 0; i < 100; i++) {
                await Task.Delay(100);
                await transcriber.AppendTranscription(new AppendTranscriptionCommand {
                    TranscriptId = transcriptId,
                    Data = new Base64Encoded(audioBytes)
                });
                
                
                var result = await transcriber.PollTranscription(new PollTranscriptionCommand(transcriptId, 0));
                Out.WriteLine(result.Length.ToString());
            }

            await transcriber.EndTranscription(new EndTranscriptionCommand(transcriptId));
            
            // result.Should().NotBeNull();
            // result.Length.Should().BePositive();
            
            // Out.WriteLine(result[0].Speech!.Text);
        }

    }
}
