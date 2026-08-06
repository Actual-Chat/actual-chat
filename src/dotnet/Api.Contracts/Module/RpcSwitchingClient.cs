using ActualLab.Rpc;
using ActualLab.Rpc.Infrastructure;

namespace ActualChat.Module;

/// <summary>
/// An <see cref="RpcClient"/> that connects via <see cref="Clients"/><c>[0]</c> and rotates
/// to the next client once the current one fails to hold a connection, wrapping around
/// after the last one.
/// </summary>
public sealed class RpcSwitchingClient : RpcClient
{
    public sealed record Options
    {
        public static readonly Options Default = new();

        public int StartIndex { get; init; }
        public int StrikesToSwitch { get; init; } = 3;
        public TimeSpan MinHealthyConnectionDuration { get; init; } = TimeSpan.FromSeconds(10);
        public MomentClock? Clock { get; init; }
        public Func<CancellationToken, Task<bool>>? ServerProbe { get; init; }
        public Func<bool>? IsBackgroundGetter { get; init; }
    }

    private MomentClock Clock => field ??= Settings.Clock ?? Hub.SystemClock;

    public Options Settings { get; }
    public RpcClient[] Clients { get; }

    public RpcSwitchingClient(IServiceProvider services, Options settings, params RpcClient[] clients)
        : base(services)
    {
        if (clients.Length == 0)
            throw new ArgumentOutOfRangeException(nameof(clients));

        Settings = settings;
        Clients = clients;
    }

    public override Task<RpcConnection> ConnectRemote(
        RpcClientPeer clientPeer,
        RpcPeerConnectionState connectionState,
        CancellationToken cancellationToken)
    {
        var state = GetOrAddState(clientPeer);
        state.ConnectedAt = default;
        state.ProbeTask = Settings.ServerProbe?.Invoke(cancellationToken);
        var client = Clients[state.Index];
        state.LastClient = client;
        return client.Connect(clientPeer, connectionState, cancellationToken);
    }

    public override void OnConnectionStateChange(
        RpcClientPeer clientPeer,
        RpcPeerConnectionState connectionState)
    {
        var state = GetOrAddState(clientPeer);
        if (connectionState.IsConnected()) {
            state.ConnectedAt = Clock.Now;
            state.ProbeTask = null;
            return;
        }
        if (connectionState.IsDisconnected())
            state.WhenDisconnectHandled = HandleDisconnected(state);
    }

    public State? GetState(RpcClientPeer clientPeer)
        => clientPeer.Extensions.KeylessGet<State>();

    public static (int Index, int Strikes) GetNextPosition(
        int index,
        int strikes,
        int clientCount,
        int strikesToSwitch,
        bool isHealthy)
    {
        if (isHealthy)
            return (index, 0);

        strikes++;
        return strikes < strikesToSwitch
            ? (index, strikes)
            : ((index + 1) % clientCount, 0);
    }

    // Protected/internal methods

    // It's internal to be accessible from tests
    internal async Task HandleDisconnected(State state)
    {
        try {
            var connectedAt = state.ConnectedAt;
            var probeTask = state.ProbeTask;
            state.ConnectedAt = default;
            state.ProbeTask = null;
            if (Settings.IsBackgroundGetter?.Invoke() == true)
                return;

            if (probeTask is not null && !await IsServerReachable(probeTask).ConfigureAwait(false)) {
                // The server is unreachable, so this disconnect says nothing about the current client.
                state.Strikes = 0;
                return;
            }

            // A transport that connects and dies seconds later still reports Connected, so only
            // the connection's lifetime tells us whether the current client actually works.
            var isHealthy = connectedAt != default
                && Clock.Now - connectedAt >= Settings.MinHealthyConnectionDuration;
            var index = state.Index;
            (state.Index, state.Strikes) = GetNextPosition(
                index, state.Strikes, Clients.Length, Settings.StrikesToSwitch, isHealthy);
            if (state.Index == index)
                return;

            Log.LogWarning("Switching RPC client: [{OldIndex}] {OldClient} -> [{NewIndex}] {NewClient}",
                index, Clients[index].GetType().Name, state.Index, Clients[state.Index].GetType().Name);
        }
        catch (Exception e) {
            Log.LogError(e, "HandleDisconnected failed");
        }
    }

    // Private methods

    private State GetOrAddState(RpcClientPeer clientPeer)
    {
        if (clientPeer.Extensions.KeylessGet<State>() is { } existing)
            return existing;

        var state = new State { Index = Settings.StartIndex % Clients.Length };
        clientPeer.Extensions.KeylessSet(state);
        return state;
    }

    private static async Task<bool> IsServerReachable(Task<bool> probeTask)
    {
        try {
            return await probeTask.ConfigureAwait(false);
        }
        catch {
            return false;
        }
    }

    // Nested types

    public sealed class State
    {
        public int Index { get; set; }
        public int Strikes { get; set; }
        public Moment ConnectedAt { get; set; }
        public RpcClient? LastClient { get; set; }
        public Task<bool>? ProbeTask { get; set; }
        public Task? WhenDisconnectHandled { get; set; }
    }
}
