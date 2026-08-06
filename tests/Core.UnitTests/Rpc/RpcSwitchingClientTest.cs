using ActualChat.Module;
using ActualLab.Rpc;
using ActualLab.Rpc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace ActualChat.Core.UnitTests.Rpc;

public class RpcSwitchingClientTest
{
    private static readonly TimeSpan ShortLife = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan HealthyLife = TimeSpan.FromSeconds(11);

    [Theory]
    // Strikes below the threshold keep the current client
    [InlineData(0, 0, 2, 3, false, 0, 1)]
    [InlineData(0, 1, 2, 3, false, 0, 2)]
    // The last strike rotates and resets the streak
    [InlineData(0, 2, 2, 3, false, 1, 0)]
    [InlineData(0, 2, 3, 3, false, 1, 0)]
    [InlineData(1, 2, 3, 3, false, 2, 0)]
    // ... and wraps around after the last client
    [InlineData(1, 2, 2, 3, false, 0, 0)]
    [InlineData(2, 2, 3, 3, false, 0, 0)]
    // A single client has nowhere to rotate to
    [InlineData(0, 2, 1, 3, false, 0, 0)]
    // A healthy connection clears the streak
    [InlineData(0, 2, 2, 3, true, 0, 0)]
    [InlineData(1, 0, 2, 3, true, 1, 0)]
    public void GetNextPositionShouldRotateOnConsecutiveStrikes(
        int index,
        int strikes,
        int clientCount,
        int strikesToSwitch,
        bool isHealthy,
        int expectedIndex,
        int expectedStrikes)
    {
        // act
        var (nextIndex, nextStrikes) = RpcSwitchingClient.GetNextPosition(
            index, strikes, clientCount, strikesToSwitch, isHealthy);

        // assert
        nextIndex.Should().Be(expectedIndex);
        nextStrikes.Should().Be(expectedStrikes);
    }

    [Fact]
    public async Task ConnectionsThatConnectButDieQuicklyShouldStillSwitch()
    {
        // arrange
        var (client, clock) = CreateClient();
        var state = new RpcSwitchingClient.State();

        // act
        await Disconnect(client, state, clock, ShortLife);
        await Disconnect(client, state, clock, ShortLife);
        var indexBeforeLastStrike = state.Index;
        await Disconnect(client, state, clock, ShortLife);

        // assert
        indexBeforeLastStrike.Should().Be(0);
        state.Index.Should().Be(1);
        state.Strikes.Should().Be(0);
    }

    [Fact]
    public async Task HealthyConnectionShouldResetStrikes()
    {
        // arrange
        var (client, clock) = CreateClient();
        var state = new RpcSwitchingClient.State();

        // act
        await Disconnect(client, state, clock, ShortLife);
        await Disconnect(client, state, clock, ShortLife);
        await Disconnect(client, state, clock, HealthyLife);
        await Disconnect(client, state, clock, ShortLife);

        // assert
        state.Index.Should().Be(0);
        state.Strikes.Should().Be(1);
    }

    [Fact]
    public async Task ConnectionAtTheHealthyThresholdShouldNotBeAStrike()
    {
        // arrange
        var (client, clock) = CreateClient();
        var state = new RpcSwitchingClient.State { Strikes = 2 };

        // act
        await Disconnect(client, state, clock, RpcSwitchingClient.Options.Default.MinHealthyConnectionDuration);

        // assert
        state.Index.Should().Be(0);
        state.Strikes.Should().Be(0);
    }

    [Fact]
    public async Task ConnectThatNeverSucceededShouldCountAsStrike()
    {
        // arrange
        var (client, _) = CreateClient();
        // ConnectedAt stays default when the connection never came up
        var state = new RpcSwitchingClient.State();

        // act
        await client.HandleDisconnected(state);
        await client.HandleDisconnected(state);
        await client.HandleDisconnected(state);

        // assert
        state.Index.Should().Be(1);
    }

    [Fact]
    public async Task BackgroundDisconnectsShouldNotCountAsStrikes()
    {
        // arrange
        var (client, clock) = CreateClient(o => o with { IsBackgroundGetter = () => true });
        var state = new RpcSwitchingClient.State();

        // act
        await Disconnect(client, state, clock, ShortLife);
        await Disconnect(client, state, clock, ShortLife);
        await Disconnect(client, state, clock, ShortLife);

        // assert
        state.Index.Should().Be(0);
        state.Strikes.Should().Be(0);
    }

    [Fact]
    public async Task UnreachableServerShouldVoidTheStrikeStreak()
    {
        // arrange
        var (client, clock) = CreateClient();
        var state = new RpcSwitchingClient.State();

        // act
        await Disconnect(client, state, clock, ShortLife);
        await Disconnect(client, state, clock, ShortLife);
        var strikesWhileReachable = state.Strikes;
        state.ProbeTask = Task.FromResult(false);
        await Disconnect(client, state, clock, ShortLife);

        // assert
        strikesWhileReachable.Should().Be(2);
        state.Index.Should().Be(0);
        state.Strikes.Should().Be(0);
    }

    [Fact]
    public async Task SwitchShouldWrapAroundToTheFirstClient()
    {
        // arrange
        var (client, clock) = CreateClient();
        var state = new RpcSwitchingClient.State { Index = 1 };

        // act
        await Disconnect(client, state, clock, ShortLife);
        await Disconnect(client, state, clock, ShortLife);
        await Disconnect(client, state, clock, ShortLife);

        // assert
        state.Index.Should().Be(0);
    }

    // Private methods

    private static async Task Disconnect(
        RpcSwitchingClient client,
        RpcSwitchingClient.State state,
        ManualClock clock,
        TimeSpan aliveFor)
    {
        state.ConnectedAt = clock.Time;
        clock.Time += aliveFor;
        await client.HandleDisconnected(state);
    }

    private static (RpcSwitchingClient Client, ManualClock Clock) CreateClient(
        Func<RpcSwitchingClient.Options, RpcSwitchingClient.Options>? configure = null,
        int clientCount = 2)
    {
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var clock = new ManualClock();
        var settings = RpcSwitchingClient.Options.Default with { Clock = clock };
        if (configure is not null)
            settings = configure.Invoke(settings);

        var clients = Enumerable.Range(0, clientCount)
            .Select(_ => (RpcClient)new StubRpcClient(services))
            .ToArray();
        return (new RpcSwitchingClient(services, settings, clients), clock);
    }

    // Nested types

    private sealed class ManualClock : MomentClock
    {
        // Starts well past the epoch: HandleDisconnected reads default(Moment) as
        // "never connected", so a clock at zero would make every connection look failed.
        public Moment Time { get; set; } = new(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        public override Moment Now => Time;
    }

    private sealed class StubRpcClient(IServiceProvider services) : RpcClient(services)
    {
        public override Task<RpcConnection> ConnectRemote(
            RpcClientPeer clientPeer,
            RpcPeerConnectionState connectionState,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
