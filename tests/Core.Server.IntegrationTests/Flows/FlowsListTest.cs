using ActualChat.Flows;
using ActualChat.Flows.Db;
using ActualChat.Testing.Host;
using ActualLab.Fusion.EntityFramework;
using ActualLab.Versioning;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

[Trait("Category", "Slow")]
public class FlowsListTest(ITestOutputHelper @out)
    : AppHostTestBase($"x-{nameof(FlowsListTest)}", TestAppHostOptions.Default, @out)
{
    [Fact(Timeout = 180_000)]
    public async Task ListReportsStatuses()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(170));
        var cancellationToken = cts.Token;

        await using var h = await NewAppHost();
        var clock = h.Services.Clocks().SystemClock;
        var now = clock.Now;

        // FlowBackend.List derives UpdatedAt and the stuck cutoff from Version, assuming
        // Version == clock-based epoch ticks. Confirm that assumption against the live generator.
        var version = h.Services.VersionGenerator<long>().NextVersion(0);
        Math.Abs(version - now.EpochOffset.Ticks).Should().BeLessThan(TimeSpan.FromSeconds(10).Ticks);

        var backend = h.Services.GetRequiredService<IFlowBackend>();
        var dbHub = h.Services.DbHub<FlowsDbContext>();

        long V(TimeSpan ago) => (now - ago).EpochOffset.Ticks;
        var prefix = $"FlowsListTest{Guid.NewGuid():N}";
        var seed = new[] {
            NewDbFlow($"{prefix}A:ok", V(TimeSpan.FromMinutes(1)), isCompleted: true, isFailed: false),
            NewDbFlow($"{prefix}A:bad", V(TimeSpan.FromMinutes(2)), isCompleted: true, isFailed: true),
            NewDbFlow($"{prefix}A:active", V(TimeSpan.FromMinutes(3)), isCompleted: false, isFailed: false),
            NewDbFlow($"{prefix}B:stuck", V(TimeSpan.FromHours(12)), isCompleted: false, isFailed: false),
        };
        await using (var dbContext = await dbHub.CreateDbContext(true, cancellationToken)) {
            dbContext.Flows.AddRange(seed);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var report = await backend.List(new FlowsQuery(Limit: 10_000), cancellationToken);

        var aggA = report.Aggregates.Single(a => a.Name == $"{prefix}A");
        aggA.Completed.Should().Be(1);
        aggA.Failed.Should().Be(1);
        aggA.Active.Should().Be(1);
        aggA.Stuck.Should().Be(0);
        report.Aggregates.Single(a => a.Name == $"{prefix}B").Stuck.Should().Be(1);

        var problematic = await backend.List(new FlowsQuery(ProblematicOnly: true, Limit: 10_000), cancellationToken);
        var ours = problematic.Rows.Where(r => r.Name.StartsWith(prefix, StringComparison.Ordinal)).ToList();
        ours.Select(r => r.FlowId).Should().BeEquivalentTo([$"{prefix}A:bad", $"{prefix}B:stuck"]);
        ours.Single(r => r.FlowId == $"{prefix}A:bad").Status.Should().Be(FlowStatus.Failed);
        ours.Single(r => r.FlowId == $"{prefix}B:stuck").Status.Should().Be(FlowStatus.Stuck);

        var typeB = await backend.List(new FlowsQuery(Name: $"{prefix}B", Limit: 10_000), cancellationToken);
        typeB.Rows.Should().OnlyContain(r => r.Name == $"{prefix}B");
    }

    private static DbFlow NewDbFlow(string id, long version, bool isCompleted, bool isFailed)
        => new() {
            Id = id,
            Version = version,
            DataVersion = 1,
            IsCompleted = isCompleted,
            IsFailed = isFailed,
            Data = [1],
            ResultData = isCompleted ? [1] : null,
            Console = "",
        };
}
