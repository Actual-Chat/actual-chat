using System.Threading;
using System.Threading.Tasks;
using ActualChat.Audio;
using ActualChat.Testing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Stl.Fusion.Authentication;
using Stl.Testing;
using Stl.Time;
using Xunit;
using Xunit.Abstractions;

namespace ActualChat.Tests.Audio
{
    public class AudioPipelineTest : TestBase
    {
        public AudioPipelineTest(ITestOutputHelper @out) : base(@out)
        {
        }

        [Fact]
        public async Task InitCompleteRecordingTest()
        {
            using var appHost = await TestHostFactory.NewAppHost();
            using var blazorTester = appHost.NewBlazorTester();
            _ = await blazorTester.SignIn(new User("", "Bob"));
            var services = appHost.Services;
            var session = blazorTester.Session;

            var audioRecorder = services.GetRequiredService<IAudioRecorder>();

            var initializeCommand = new InitializeAudioRecorderCommand {
                Session = session,
                Language = "RU-ru",
                AudioFormat = new AudioFormat {
                    Codec = AudioCodec.Opus,
                    ChannelCount = 1,
                    SampleRate = 48_000
                },
                ClientStartOffset = CpuClock.Now
            };
            var initResult = await audioRecorder.Initialize(initializeCommand, CancellationToken.None);
            initResult.Value.Should().NotBeNullOrWhiteSpace();

            var recordingId = initResult;
            await audioRecorder.Complete(new CompleteAudioRecording(recordingId));
        }
        
        
    }
}