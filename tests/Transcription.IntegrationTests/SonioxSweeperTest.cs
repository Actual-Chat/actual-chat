using ActualChat.Module;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;

namespace ActualChat.Transcription.IntegrationTests;

[Collection(nameof(TranscriptionCollection))]
public class SonioxSweeperTest(ITestOutputHelper @out, ILogger<SonioxSweeperTest> log)
    : TranscriberTestBase(@out, log)
{
    [Fact(Timeout = 60_000)]
    public async Task SweepDeletesOrphansAndKeepsRecentOnes()
    {
        // arrange
        var services = CreateServices();
        if (services.GetRequiredService<CoreServerSettings>().SonioxKey.IsNullOrEmpty()) {
            WriteLine("CoreSettings__SonioxKey is not set - skipping.");
            return;
        }

        var client = services.GetRequiredService<SonioxClient>();
        string orphanId;
        var stream = File.OpenRead(GetAudioFilePath("0004-AK-recoded.opus"));
        await using (stream.ConfigureAwait(false)) {
            try {
                orphanId = await client.UploadFile(stream, "orphan.opus", CancellationToken.None);
            }
            catch (Exception e) when (IsExternalQuotaExceeded(e)) {
                return;
            }
        }
        WriteLine($"Uploaded orphan {orphanId}");

        // act - retention shorter than the upload's age, so the fresh file is the orphan here
        var sweeper = NewSweeper(services, TimeSpan.Zero);
        var deleted = await sweeper.Sweep(CancellationToken.None);

        // assert
        WriteLine($"Swept {deleted} file(s)");
        deleted.Should().BeGreaterThanOrEqualTo(1);
        var remaining = await client.ListFiles(null, 1000, CancellationToken.None);
        (remaining.Files ?? []).Should().NotContain(x => x.Id == orphanId);
    }

    [Fact(Timeout = 60_000)]
    public async Task SweepKeepsFilesInsideRetention()
    {
        // arrange
        var services = CreateServices();
        if (services.GetRequiredService<CoreServerSettings>().SonioxKey.IsNullOrEmpty()) {
            WriteLine("CoreSettings__SonioxKey is not set - skipping.");
            return;
        }

        var client = services.GetRequiredService<SonioxClient>();
        var cleaner = services.GetRequiredService<SonioxCleaner>();
        string keptId;
        var stream = File.OpenRead(GetAudioFilePath("0004-AK-recoded.opus"));
        await using (stream.ConfigureAwait(false)) {
            try {
                keptId = await client.UploadFile(stream, "in-flight.opus", CancellationToken.None);
            }
            catch (Exception e) when (IsExternalQuotaExceeded(e)) {
                return;
            }
        }

        // act - the default retention must protect an upload a transcription is still using
        var sweeper = NewSweeper(services, new SonioxSweeper.Options().Retention);
        var deleted = await sweeper.Sweep(CancellationToken.None);

        // assert
        deleted.Should().Be(0);
        var remaining = await client.ListFiles(null, 1000, CancellationToken.None);
        (remaining.Files ?? []).Should().Contain(x => x.Id == keptId);

        cleaner.Enqueue(null, keptId);
        await cleaner.Flush().WaitAsync(TimeSpan.FromSeconds(30));
    }

    // Private methods

    private static SonioxSweeper NewSweeper(IServiceProvider services, TimeSpan retention)
        => new(new SonioxSweeper.Options { Retention = retention }, services);

    private IServiceProvider CreateServices()
    {
        IConfiguration configuration = new ConfigurationManager {
            Sources = { new EnvironmentVariablesConfigurationSource() },
        };
        return new ServiceCollection()
            .AddSingleton<IConfiguration>(_ => configuration)
            .AddSingleton(MomentClockSet.Default)
            .AddSingleton(_ => configuration.Settings<CoreServerSettings>(nameof(CoreSettings)))
            .AddSoniox()
            .AddTestLogging(Out)
            .BuildServiceProvider();
    }
}
