namespace ActualChat;

public static class BitArrayExt
{
    public static string Format(this BitArray source)
        => string.Create(source.Length, source, static (span, source1) => {
            for (var i = 0; i < span.Length; i++)
                span[i] = source1[i] ? '1' : '0';
        });
}
