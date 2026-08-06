using System.Text;
using ActualChat.Kvas;
using ActualChat.Testing.Host;
using ActualChat.UI.Blazor.Services;

// ReSharper disable WithExpressionModifiesAllMembers

namespace ActualChat.UI.Blazor.IntegrationTests;

[Collection(nameof(UICollection))]
public class LogUITest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private BlazorTester Tester => field ??= AppHost.NewBlazorTester(Out);
    private LogUI LogUI => field ??= Tester.ScopedAppServices.GetRequiredService<LogUI>();
    private LocalSettings LocalSettings => field ??= Tester.Services.LocalSettings();
    private ILogger ScopedLog => field ??= Tester.ScopedAppServices.LogFor(GetType());

    protected override Task InitializeAsync()
    {
        LogUI.DiagLog = Out.ToLogger<LogUI>();
        return base.InitializeAsync();
    }

    [Fact]
    public async Task ShouldReturnLogEntries()
    {
        // arrange
        await Tester.SignInAsBob();
        await SetIsEnabled(true);
        await LogUI.WhenReady;
        ScopedLog.LogInformation($"{nameof(ShouldReturnLogEntries)}: Hello, Info!");
        ScopedLog.LogWarning($"{nameof(ShouldReturnLogEntries)}: Hello, Warning!");
        ScopedLog.LogError($"{nameof(ShouldReturnLogEntries)}: Hello, Error!");
        ScopedLog.LogCritical($"{nameof(ShouldReturnLogEntries)}: Hello, Critical!");

        // act
        var tiles = await GetTiles(4);

        // assert
        tiles.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ShouldReturnEmptyIfDisabled()
    {
        // arrange
        await Tester.SignInAsBob();
        await SetIsEnabled(false);
        await LogUI.WhenReady;
        ScopedLog.LogInformation($"{nameof(ShouldReturnLogEntries)}: Hello, Info!");

        // act
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;
        var idRange = await LogUI.GetIdRange(cancellationToken);

        // assert
        idRange.IsEmpty.Should().BeTrue();

        // act
        var tiles = await LogUI.GetTiles(new (5, 10), cancellationToken);

        // assert
        tiles.Should().BeEmpty();
    }

    [Fact]
    public async Task TilesShouldNotContainIdGaps()
    {
        // arrange
        await Tester.SignInAsBob();
        await SetIsEnabled(true);
        await LogUI.WhenReady;
        for (int i = 0; i < 1000; i++) {
            ScopedLog.LogDebug($"{nameof(ShouldReturnLogEntries)}: Hello, Debug!");
            ScopedLog.LogInformation($"{nameof(ShouldReturnLogEntries)}: Hello, Info!");
            ScopedLog.LogWarning($"{nameof(ShouldReturnLogEntries)}: Hello, Warning!");
            ScopedLog.LogError($"{nameof(ShouldReturnLogEntries)}: Hello, Error!");
            ScopedLog.LogCritical($"{nameof(ShouldReturnLogEntries)}: Hello, Critical!");
        }

        // act
        var tiles = await GetTiles(5000);

        // assert
        tiles.Should().NotBeEmpty();
        var expectedRangeStartId = 0L;
        var expectedId = 1L;
        foreach (var tile in tiles) {
            tile.IdRange.Start.Should().Be(expectedRangeStartId);
            foreach (var entry in tile.Entries) {
                entry.Id.Should().Be(expectedId);
                expectedId++;
            }
            expectedRangeStartId = tile.IdRange.End;
        }
    }

     private Task<IReadOnlyList<LogTile>> GetTiles(int minExpectedEntryCount, bool mustPrintTiles = false)
         => ComputedTest.When(async ct => {
             var idRange = await LogUI.GetIdRange(ct);
             var tiles = await LogUI.GetTiles(idRange, ct);
             if (mustPrintTiles) {
                 var sb = new StringBuilder().AppendLine($"!!! Found {tiles.Count} tiles for #{idRange}");
                 foreach (var tile in tiles) {
                     sb.AppendLine($"  Tile: {tile.IdRange}");
                     foreach (var entry in tile.Entries)
                         sb.AppendLine($"    #{entry.Id}: {entry.LogLevel} {entry.CategoryName}: {entry.Message}").AppendLine(entry.Exception?.ToString());
                 }
                 Log.LogInformation(sb.ToString());
             }
             var foundEntries = tiles.SelectMany(x => x.Entries).Where(x => x.CategoryName.Contains(nameof(LogUITest)));
             foundEntries.Should().HaveCount(minExpectedEntryCount);
             return tiles;
         }, TimeSpan.FromSeconds(10).Debuggable());

     private async Task SetIsEnabled(bool value)
     {
         await LocalSettings.LocalAppSettings().Update(x => x with { IsLogViewerEnabled = value });
         LogUI.IsEnabled.Invalidate();
         await TestExt.When(() => LogUI.IsEnabled.Value.Should().Be(value), TimeSpan.FromSeconds(10));
     }
}
