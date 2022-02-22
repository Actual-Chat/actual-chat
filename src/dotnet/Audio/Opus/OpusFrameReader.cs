namespace ActualChat.Audio.Opus;

public class OpusFrameReader
{
    private static readonly float[] _silkFrameSizeMap = { 10, 20, 40, 60 };
    private static readonly float[] _hybridFrameSizeMap = { 10, 20 };
    private static readonly float[] _celtFrameSizeMap = { 2.5f, 5, 10, 20 };

    // public async IAsyncEnumerable<OpusFrame> Parse(
    //     IAsyncEnumerable<byte[]> byteStream,
    //     CancellationToken cancellationToken)
    // {
    //     // var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
    //     // var memory = buffer.AsMemory(0, bufferSize);
    //     // try {
    //     //     var bytesRead = await source.ReadAsync(memory, cancellationToken).ConfigureAwait(false);
    //     //     while (bytesRead != 0) {
    //     //         yield return buffer[..bytesRead];
    //     //         bytesRead = await source.ReadAsync(memory, cancellationToken).ConfigureAwait(false);
    //     //     }
    //     // }
    //     // finally {
    //     //     ArrayPool<byte>.Shared.Return(buffer);
    //     // }
    //
    //     await foreach (var block in byteStream.WithCancellation(cancellationToken).ConfigureAwait(false)) {
    //
    //     }
    // }
    //
    // private IEnumerable<OpusFrame> Parse(Span<byte> block, ref Memory<byte> remainder)
    // {
    //     Span<byte> combined;
    //     if (remainder.Length == 0)
    //         combined = block;
    //     else {
    //         combined = new byte[block.Length + remainder.Length].AsSpan();
    //         remainder.Span.CopyTo(combined);
    //         block.CopyTo(combined[remainder.Length..]);
    //     }
    //     var position = 0;
    //     while (position < combined.Length) {
    //         const byte configMask = 0b11111000;
    //         const byte cMask = 0b11;
    //         var toc = combined[position];
    //         var config = toc & configMask;
    //         // var duration = config switch {
    //         //     >=0 and <=3 => _silkFrameSizeMap[config],
    //         //     >=4 and <=7 => _silkFrameSizeMap[config - 4],
    //         //     >=8 and <=11 => _silkFrameSizeMap[config - 8],
    //         //     >=12 and <=13 => _hybridFrameSizeMap[config - 12],
    //         //     >=14 and <=15 => _hybridFrameSizeMap[config - 14],
    //         //     >=16 and <=19 => _celtFrameSizeMap[config - 16],
    //         //     >=20 and <=23 => _celtFrameSizeMap[config - 20],
    //         //     >=24 and <=27 => _celtFrameSizeMap[config - 24],
    //         //     >=27 and <=31 => _celtFrameSizeMap[config - 27],
    //         //     _ => throw new ArgumentOutOfRangeException("toc.config"),
    //         // };
    //
    //     }
    // }
}
