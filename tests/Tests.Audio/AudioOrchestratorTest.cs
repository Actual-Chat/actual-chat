using System.Threading;
using System.Threading.Tasks;
using ActualChat.Audio;
using ActualChat.Testing;
using Microsoft.Extensions.DependencyInjection;
using Stl.Testing;
using Xunit;
using Xunit.Abstractions;

namespace ActualChat.Tests.Audio
{
    public class AudioOrchestratorTest : TestBase
    {
        public AudioOrchestratorTest(ITestOutputHelper @out) : base(@out)
        {
            
        }

        [Fact]
        public async Task EmptyRecordingTest()
        {
            using var appHost = await TestHostFactory.NewAppHost();
            var services = appHost.Services;
            var orchestrator = services.GetRequiredService<AudioOrchestrator>();
            var cts = new CancellationTokenSource();
            await orchestrator.WaitForNewRecording(cts.Token);
        }
    }
}