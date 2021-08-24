using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ActualChat.Audio;
using ActualChat.Transcription;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Stl.Serialization;
using Stl.Testing;
using Xunit;
using Xunit.Abstractions;

namespace ActualChat.Tests.Transcription
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
            var feedTask = FeedTranscriber(transcriber);
            var cts = new CancellationTokenSource();
            var pollTask = PollResults(transcriber, cts.Token);

            await feedTask;
            //     

            await transcriber.EndTranscription(new EndTranscriptionCommand(transcriptId));
            
            

            await pollTask;


            async Task FeedTranscriber(ITranscriber t)
            {
                await foreach (var chunk in ReadAudioFileSimulatingSpeech())
                    await t.AppendTranscription(new AppendTranscriptionCommand {
                        TranscriptId = transcriptId,
                        Data = chunk
                    });
            }
        

            async Task PollResults(ITranscriber t, CancellationToken token)
            {
                var index = 0;
                while (true) {
                    if (token.IsCancellationRequested)
                        break;
            
                    var result = await t.PollTranscription(new PollTranscriptionCommand(transcriptId, index), token);
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
        }

        private async IAsyncEnumerable<Base64Encoded> ReadAudioFileSimulatingSpeech()
        {
            await using var inputStream = new FileStream(Path.Combine(Environment.CurrentDirectory, "data", "file.webm"), FileMode.Open, FileAccess.Read);
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
