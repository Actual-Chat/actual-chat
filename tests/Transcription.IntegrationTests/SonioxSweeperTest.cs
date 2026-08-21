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
        await using (stream.ConfigureAwait(false))
            orphanId = await client.UploadFile(stream, "orphan.opus", CancellationToken.None);
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
        await using (stream.ConfigureAwait(false))
            keptId = await client.UploadFile(stream, "in-flight.opus", CancellationToken.None);

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

    [Fact(Timeout = 120_000)]
    public async Task SweepDeletesOrphanedTranscriptions()
    {
        // arrange
        var services = CreateServices();
        if (services.GetRequiredService<CoreServerSettings>().SonioxKey.IsNullOrEmpty()) {
            WriteLine("CoreSettings__SonioxKey is not set - skipping.");
            return;
        }

        var client = services.GetRequiredService<SonioxClient>();
        await RequireSonioxCapacity(client, "0004-AK-recoded.opus");

        string fileId;
        var stream = File.OpenRead(GetAudioFilePath("0004-AK-recoded.opus"));
        await using (stream.ConfigureAwait(false))
            fileId = await client.UploadFile(stream, "orphan.opus", CancellationToken.None);
        var request = new Dictionary<string, object?> {
            ["file_id"] = fileId,
            ["model"] = SonioxOfflineTranscriber.Model,
        };
        var transcriptionId = await client.CreateTranscription(request, CancellationToken.None);
        await WaitForTerminalStatus(client, transcriptionId);
        WriteLine($"Created transcription {transcriptionId} of file {fileId}");

        // act - retention shorter than the transcription's age, so it's the orphan here
        var sweeper = NewSweeper(services, TimeSpan.Zero);
        var deleted = await sweeper.Sweep(CancellationToken.None);

        // assert - and the transcription's delete takes its file with it
        WriteLine($"Swept {deleted} item(s)");
        deleted.Should().BeGreaterThanOrEqualTo(1);
        var transcriptions = await client.ListTranscriptions(null, 1000, CancellationToken.None);
        (transcriptions.Transcriptions ?? []).Should().NotContain(x => x.Id == transcriptionId);
        var files = await client.ListFiles(null, 1000, CancellationToken.None);
        (files.Files ?? []).Should().NotContain(x => x.Id == fileId);
    }

    // Private methods

    private static async Task WaitForTerminalStatus(SonioxClient client, string transcriptionId)
    {
        // A transcription Soniox is still working on is one it refuses to delete, so the sweep can
        // only be measured once this one settles.
        var endsAt = CpuTimestamp.Now + TimeSpan.FromSeconds(60);
        while (CpuTimestamp.Now < endsAt) {
            var status = await client.GetTranscriptionStatus(transcriptionId, CancellationToken.None);
            if (status.Status is "completed" or "error")
                return;

            await Task.Delay(TimeSpan.FromSeconds(1));
        }
        throw StandardError.Timeout($"Transcription {transcriptionId} didn't finish.");
    }

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
