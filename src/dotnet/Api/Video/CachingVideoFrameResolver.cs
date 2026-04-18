using MessagePack;
using MessagePack.Formatters;

namespace ActualChat.Video;

/// <summary>
/// MessagePack resolver that supplies <see cref="CachingVideoFrameFormatter"/> for <see cref="VideoFrame"/>.
/// Returns <c>null</c> for all other types so the standard resolver chain continues.
/// </summary>
/// <remarks>
/// Prepend to <c>DefaultMessagePackResolver.Resolvers</c> (see <c>ApiModuleInitializer</c>).
/// <see cref="VideoFrame"/>[] batches automatically use this formatter via MessagePack's built-in
/// array formatter, which per-element delegates to the resolver.
/// </remarks>
public sealed class CachingVideoFrameResolver : IFormatterResolver
{
    public static readonly CachingVideoFrameResolver Instance = new();

    private CachingVideoFrameResolver()
    { }

    public IMessagePackFormatter<T>? GetFormatter<T>()
        => typeof(T) == typeof(VideoFrame)
            ? (IMessagePackFormatter<T>)(object)CachingVideoFrameFormatter.Instance
            : null;
}
