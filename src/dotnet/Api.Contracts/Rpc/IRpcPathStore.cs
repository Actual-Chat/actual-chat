namespace ActualChat.Rpc;

/// <summary>
/// Where a network's check history lives between runs. The implementation keeps the number of
/// stored networks bounded.
/// </summary>
public interface IRpcPathStore
{
    ValueTask<byte[]?> Load(string networkId, CancellationToken cancellationToken);
    ValueTask Save(string networkId, byte[] data, CancellationToken cancellationToken);
}
