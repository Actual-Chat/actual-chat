using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ActualChat.Audio;
using ActualChat.Transcription;
using FluentAssertions;
using Google.Cloud.Speech.V1P1Beta1;
using Google.Protobuf;
using Microsoft.Extensions.Logging.Abstractions;
using NetBox.Extensions;
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
                    SampleRate = 48000
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
            await transcriber.AppendTranscription(new AppendTranscriptionCommand {
                TranscriptId = transcriptId,
                Data = new Base64Encoded(audioBytes)
            });


            // for (int i = 0; i < 100; i++) {
            //     await Task.Delay(100);
            //     
            //     var result = await transcriber.PollTranscription(new PollTranscriptionCommand(transcriptId, 0));
            //     Out.WriteLine(result.Length.ToString());
            // }

            await transcriber.EndTranscription(new EndTranscriptionCommand(transcriptId));
            
            // result.Should().NotBeNull();
            // result.Length.Should().BePositive();
            
            // Out.WriteLine(result[0].Speech!.Text);
        }

        [Fact]
        public async Task GoogleRecognizeTest()
        {
            var audioBytes = await File.ReadAllBytesAsync(Path.Combine(Environment.CurrentDirectory, "data", "file.webm"));
            var audio = RecognitionAudio.FromBytes(audioBytes);
            var client = await SpeechClient.CreateAsync();
            var config = new RecognitionConfig
            {
                Encoding = RecognitionConfig.Types.AudioEncoding.WebmOpus,
                SampleRateHertz = 48000,
                LanguageCode = LanguageCodes.Russian.Russia,
                EnableAutomaticPunctuation = true
            };
            var response = await client.RecognizeAsync(config, audio);
            Out.WriteLine(response.ToString());
        }
        
        // [Fact(Skip = "Manual")]
        [Fact]
        public async Task GoogleStreamedRecognizeTest()
        {
            var audioBytes = await File.ReadAllBytesAsync(Path.Combine(Environment.CurrentDirectory, "data", "file.webm"));
            var client = await SpeechClient.CreateAsync();
            var config = new RecognitionConfig
            {
                Encoding = RecognitionConfig.Types.AudioEncoding.WebmOpus,
                SampleRateHertz = 48000,
                LanguageCode = LanguageCodes.Russian.Russia,
                EnableAutomaticPunctuation = true,
                EnableWordConfidence = true,
                EnableWordTimeOffsets = true
            };
            var streamingRecognize = client.StreamingRecognize();
            await streamingRecognize.WriteAsync(new StreamingRecognizeRequest {
                StreamingConfig = new StreamingRecognitionConfig {
                    Config = config,
                    InterimResults = true,
                    SingleUtterance = false
                }
            });
            
            var writeTask = WriteToStream(streamingRecognize, audioBytes);
            
            await foreach (var response in streamingRecognize.GetResponseStream()) {
                if (response.Error != null)
                    Out.WriteLine(response.Error.Message);
                else
                    Out.WriteLine(response.ToString());
            }

            await writeTask;
        }
        
        [Fact]
        public async Task GoogleMultiFileStreamedRecognizeTest()
        {
            var audio1 = await File.ReadAllBytesAsync(Path.Combine(Environment.CurrentDirectory, "data", "1.webm"));
            var audio2 = await File.ReadAllBytesAsync(Path.Combine(Environment.CurrentDirectory, "data", "2.webm"));
            var audio3 = await File.ReadAllBytesAsync(Path.Combine(Environment.CurrentDirectory, "data", "3.webm"));
            var audio4 = await File.ReadAllBytesAsync(Path.Combine(Environment.CurrentDirectory, "data", "4.webm"));
            var audio56 = await File.ReadAllBytesAsync(Path.Combine(Environment.CurrentDirectory, "data", "56.webm"));
            var audio789 = await File.ReadAllBytesAsync(Path.Combine(Environment.CurrentDirectory, "data", "789.webm"));
            var audioboy = await File.ReadAllBytesAsync(Path.Combine(Environment.CurrentDirectory, "data", "boy.webm"));
            var client = await SpeechClient.CreateAsync();
            var config = new RecognitionConfig
            {
                Encoding = RecognitionConfig.Types.AudioEncoding.WebmOpus,
                SampleRateHertz = 48000,
                LanguageCode = LanguageCodes.Russian.Russia,
                EnableAutomaticPunctuation = true,
                EnableWordConfidence = true,
                EnableWordTimeOffsets = true
            };
            var streamingRecognize = client.StreamingRecognize();
            await streamingRecognize.WriteAsync(new StreamingRecognizeRequest {
                StreamingConfig = new StreamingRecognitionConfig {
                    Config = config,
                    InterimResults = true,
                    SingleUtterance = false
                }
            });
            
            var writeTask = WriteToStream(streamingRecognize, audio1,audio2,audio3,audio4,audio56,audio789,audioboy);
            
            await foreach (var response in streamingRecognize.GetResponseStream()) {
                if (response.Error != null)
                    Out.WriteLine(response.Error.Message);
                else
                    Out.WriteLine(response.ToString());
            }

            await writeTask;
        }

        private async Task WriteToStream(SpeechClient.StreamingRecognizeStream stream,  params byte[][] byteArrays)
        {
            foreach (var bytes in byteArrays) {
                foreach (var chunk in bytes.Chunk(200)) {
                    await Task.Delay(30);
                    await stream.WriteAsync(new StreamingRecognizeRequest {
                        AudioContent = ByteString.CopyFrom(chunk.ToArray())
                    });
                }
            }

            await stream.WriteCompleteAsync();
        }
    }
}

