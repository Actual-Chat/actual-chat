namespace ActualChat.Rpc;

/// <summary>
/// What the app that hosts the client says about its own surroundings: the relays to choose from,
/// the origin first, where the history of each network is kept, and which network it is on now.
/// </summary>
public sealed record RpcPathSettings(
    IReadOnlyList<string> Candidates,
    IRpcPathStore Store,
    Func<string> GetNetworkId);
