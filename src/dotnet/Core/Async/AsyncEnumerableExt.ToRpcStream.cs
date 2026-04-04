using ActualLab.Rpc;

namespace ActualChat;

public static partial class AsyncEnumerableExt
{
    public static RpcStream<T> ToRpcStream<T>(this IAsyncEnumerable<T> source, bool allowReconnect = true)
        => RpcStream.New(source, allowReconnect);

    public static RpcStream<T> ToRpcStream<T>(this IEnumerable<T> source, bool allowReconnect = true)
        => RpcStream.New(source, allowReconnect);

    public static RpcStream<T> ToRpcStream<T>(
        this AsyncMemoizer<T> memoizer,
        CancellationToken cancellationToken = default)
        => memoizer.ToRpcStream(allowReconnect: true, cancellationToken);

    public static RpcStream<T> ToRpcStream<T>(
        this AsyncMemoizer<T> memoizer,
        bool allowReconnect,
        CancellationToken cancellationToken = default)
        => RpcStream.New(memoizer.Replay(cancellationToken), allowReconnect);

    public static RpcStream<T> ToRpcStream<T>(
        this AsyncMemoizer<T> memoizer,
        int tailSize,
        CancellationToken cancellationToken = default)
        => memoizer.ToRpcStream(tailSize, allowReconnect: true, cancellationToken);

    public static RpcStream<T> ToRpcStream<T>(
        this AsyncMemoizer<T> memoizer,
        int tailSize,
        bool allowReconnect = true,
        CancellationToken cancellationToken = default)
        => RpcStream.New(memoizer.Replay(tailSize, cancellationToken), allowReconnect);
}
