namespace ActualChat.Rpc;

public interface IRpcPathProbe
{
    // Connects to the host and moves enough data over RPC to prove it carries traffic.
    // What it measured, or null when it doesn't.
    Task<RpcPathMeasurement?> Check(string host, CancellationToken cancellationToken);

    // What the relay says about this client's IP, for the origin to learn it: the origin can't
    // see the IP through a relay. Null when the host isn't a relay or doesn't answer.
    Task<string?> GetAttestation(string host, CancellationToken cancellationToken);
}
