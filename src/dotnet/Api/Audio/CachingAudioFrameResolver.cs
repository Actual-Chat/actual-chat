using MessagePack;
using MessagePack.Formatters;

namespace ActualChat.Audio;

/// <summary>
/// MessagePack resolver that supplies <see cref="CachingAudioFrameFormatter"/> for <see cref="AudioFrame"/>.
/// Returns <c>null</c> for all other types so the standard resolver chain continues.
/// </summary>
/// <remarks>
/// Prepend to <c>DefaultMessagePackResolver.Resolvers</c> (see <c>ApiModuleInitializer</c>).
/// <see cref="AudioFrame"/>[] batches automatically use this formatter via MessagePack's built-in
/// array formatter, which per-element delegates to the resolver.
/// </remarks>
public sealed class CachingAudioFrameResolver : IFormatterResolver
{
    public static readonly CachingAudioFrameResolver Instance = new();

    private CachingAudioFrameResolver()
    { }

    public IMessagePackFormatter<T>? GetFormatter<T>()
        => typeof(T) == typeof(AudioFrame)
            ? (IMessagePackFormatter<T>)(object)CachingAudioFrameFormatter.Instance
            : null;
}
