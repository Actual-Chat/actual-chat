using ActualChat.Flows;
using ActualChat.Testing.Host;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

public class FlowsTest(ITestOutputHelper @out)
    : AppHostTestBase($"x-{nameof(FlowsTest)}", TestAppHostOptions.Default with {
        ConfigureServices = (_, services) => services.AddFlows().Add<TimerFlow>(),
    }, @out)
{
    [Fact]
    public async Task TimerFlowTest()
    {
        using var h = await NewAppHost();

        var flows = h.Services.GetRequiredService<IFlows>();
        var f0 = await flows.GetOrStart<TimerFlow>("1");
        Out.WriteLine($"[+] {f0}");

        await ComputedTest.When(async ct => {
            var flow = await flows.Get(f0.Id, ct);
            Out.WriteLine($"[*] {flow?.ToString() ?? "null"}");
            flow.Should().BeNull();
        }, TimeSpan.FromSeconds(30));
        Out.WriteLine($"[-] {f0.Id}");
    }
}
