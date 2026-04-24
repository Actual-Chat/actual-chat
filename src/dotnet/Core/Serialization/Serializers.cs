namespace ActualChat.Serialization;

/// <summary>
/// Central serializer facade for ActualChat. Thin wrapper around the ActualLab defaults so
/// application code can read/write via <c>Serializers.MessagePack</c> etc. instead of
/// reaching for framework-level statics.
/// </summary>
public static class Serializers
{
    public static ITextSerializer SystemJson => SystemJsonSerializer.Default;
    public static ITextSerializer SystemJsonTypeDecorating => SystemJsonSerializer.DefaultTypeDecorating;

    public static ITextSerializer NewtonsoftJson => NewtonsoftJsonSerializer.Default;
    public static ITextSerializer NewtonsoftJsonTypeDecorating => NewtonsoftJsonSerializer.DefaultTypeDecorating;

    public static IByteSerializer MemoryPack => MemoryPackByteSerializer.Default;
    public static IByteSerializer MemoryPackTypeDecorating => MemoryPackByteSerializer.DefaultTypeDecorating;

    public static IByteSerializer MessagePack => MessagePackByteSerializer.Default;
    public static IByteSerializer MessagePackTypeDecorating => MessagePackByteSerializer.DefaultTypeDecorating;
}
