using System.Diagnostics.CodeAnalysis;
using System.Text;
using ActualChat.Testing.Host;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.IntegrationTests;

[Collection(nameof(UICollection))]
public class LogUITest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [field: AllowNull, MaybeNull]
    private BlazorTester Tester => field ??= AppHost.NewBlazorTester(Out);
    [field: AllowNull, MaybeNull]
    private LogUI LogUI => field ??= Tester.ScopedAppServices.GetRequiredService<LogUI>();
    [field: AllowNull, MaybeNull]
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
        await Tester.SignInAsBobAdmin();
        LogUI.IsEnabled.Value = true;
        await TestExt.When(() => LogUI.IsEnabled.ValueOrDefault.Should().BeTrue(), TimeSpan.FromSeconds(10));
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
        await Tester.SignInAsBobAdmin();
        LogUI.IsEnabled.Value = false;
        await TestExt.When(() => LogUI.IsEnabled.ValueOrDefault.Should().BeFalse(), TimeSpan.FromSeconds(10));
        await LogUI.WhenReady;
        ScopedLog.LogInformation($"{nameof(ShouldReturnLogEntries)}: Hello, Info!");;

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
    public async Task ShouldNotBecomeReadyIfNotAdmin()
    {
        // arrange
        await Tester.SignInAsBob();
        await LogUI.WhenReady.AsAsyncFunc().Should().NotCompleteWithinAsync(TimeSpan.FromSeconds(1));
        ScopedLog.LogInformation($"{nameof(ShouldNotBecomeReadyIfNotAdmin)}: Hello, Info!");

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
        await Tester.SignInAsBobAdmin();
        LogUI.IsEnabled.Value = true;
        await TestExt.When(() => LogUI.IsEnabled.ValueOrDefault.Should().BeTrue(), TimeSpan.FromSeconds(10));
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
}
