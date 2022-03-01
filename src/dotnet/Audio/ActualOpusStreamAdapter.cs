using System.Buffers;
using ActualChat.Spans;

namespace ActualChat.Audio;

public class ActualOpusStreamAdapter : IAudioStreamAdapter
{
    private static readonly byte[] ActualOpusStreamHeader = { 0x41, 0x5F, 0x4F, 0x50, 0x55, 0x53, 0x5F, 0x53 }; // A_OPUS_S
    private static readonly byte[] ActualOpusStreamFormat = { 0x41, 0x5F, 0x4F, 0x50, 0x55, 0x53, 0x5F, 0x53, 0x01 }; // A_OPUS_S + version = 1
    private readonly ILogger _log;

    public ActualOpusStreamAdapter(ILogger log)
        => _log = log;

    public Task<AudioSource> Read(IAsyncEnumerable<byte[]> byteStream, CancellationToken cancellationToken)
    {
        var formatTask = TaskSource.New<AudioFormat>(true).Task;
        var formatTaskSource = TaskSource.For(formatTask);
        var readBuffer = ArrayBuffer<byte>.Lease(false, 2 * 1024);

        // We're doing this fairly complex processing via tasks & channels only
        // because "async IAsyncEnumerable<..>" methods can't contain
        // "yield return" inside "catch" blocks, and we need this here.
        var target = Channel.CreateBounded<AudioFrame>(new BoundedChannelOptions(128) {
            SingleWriter = true,
            SingleReader = true,
            AllowSynchronousContinuations = true,
            FullMode = BoundedChannelFullMode.Wait,
        });

        var _ = BackgroundTask.Run(async () => {
            try {
                var buffered = 0;
                var position = 0;
                var offsetMs = -1;
                var audioFrames = new List<AudioFrame>();
                await foreach (var data in byteStream.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                    Buffer(ref readBuffer, data, ref position, ref buffered);

                    if (!formatTask.IsCompleted) {
                        if (buffered < ActualOpusStreamHeader.Length + 1)
                            continue;

                        if (!readBuffer.Buffer.StartsWith(ActualOpusStreamHeader))
                            throw new InvalidOperationException("Actual Opus stream header is invalid.");

                        var version = readBuffer.Buffer[ActualOpusStreamHeader.Length];
                        if (version != 1)
                            throw new NotSupportedException($"Actual Opus stream version is invalid - ${version}. Only version 1 is supported.");

                        formatTaskSource.SetResult(AudioSource.DefaultFormat);
                        position = ActualOpusStreamFormat.Length + 1;
                    }


                    position += ReadFrames(readBuffer.Span[position..buffered], audioFrames, ref offsetMs);
                    foreach (var audioFrame in audioFrames)
                        await target.Writer.WriteAsync(audioFrame, cancellationToken).ConfigureAwait(false);
                    audioFrames.Clear();

                    void Buffer(ref ArrayBuffer<byte> buffer, byte[] data1, ref int position1, ref int buffered1)
                    {
                        var bufferedLength = buffered1 - position1;
                        var newLength = bufferedLength + data.Length;
                        buffer.EnsureCapacity(newLength);
                        buffer.Count = newLength;
                        if (position1 > 0) {
                            var remainder = buffer.Span[position1..buffered1];
                            remainder.CopyTo(buffer.Span);
                        }
                        data1.CopyTo(buffer.Span[bufferedLength..]);
                        position1 = 0;
                        buffered1 = newLength;
                    }

                    int ReadFrames(ReadOnlySpan<byte> buffer, List<AudioFrame> frames1, ref int offsetMs1)
                    {
                        var reader = new SpanReader(buffer);
                        var packetSize = reader.ReadVInt(4);
                        while (packetSize.HasValue && reader.Position + (int)packetSize.Value.Value < reader.Length) {
                            var packet = reader.ReadBytes((int)packetSize.Value.Value);
                            if (packet == null)
                                return reader.Position;

                            offsetMs1 += 20; // 20-ms frames
                            if (offsetMs1 >= 0)
                                frames1.Add(new AudioFrame {
                                    Data = packet!,
                                    Offset = TimeSpan.FromMilliseconds(offsetMs1),
                                });
                            packetSize = reader.ReadVInt();
                        }
                        if (!packetSize.HasValue && reader.Position + 4 < reader.Length)
                            throw new InvalidOperationException("Unable to read Opus packet length.");

                        return reader.Position;
                    }
                }
            }
            catch (OperationCanceledException e) {
                target.Writer.TryComplete(e);
                if (cancellationToken.IsCancellationRequested)
                    formatTaskSource.TrySetCanceled(cancellationToken);
                else
                    formatTaskSource.TrySetCanceled();
                throw;
            }
            catch (Exception e) {
                _log.LogError(e, "Actual Opus stream Parse failed");
                target.Writer.TryComplete(e);
                formatTaskSource.TrySetException(e);
                throw;
            }
            finally {
                target.Writer.TryComplete();
                if (!formatTask.IsCompleted)
                    formatTaskSource.TrySetException(new InvalidOperationException("Format wasn't parsed."));
                readBuffer.Release();
            }
        }, CancellationToken.None);

        var audioSource = new AudioSource(formatTask,
            target.Reader.ReadAllAsync(cancellationToken),
            TimeSpan.Zero,
            _log,
            cancellationToken);
        return Task.FromResult(audioSource);
    }

    public async IAsyncEnumerable<byte[]> Write(AudioSource source, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var bufferLease = MemoryPool<byte>.Shared.Rent(4 * 1024);
        var buffer = bufferLease.Memory;
        yield return ActualOpusStreamFormat;

        var position = 0;
        await foreach (var frame in source.GetFrames(cancellationToken).ConfigureAwait(false)) {
            position += WriteFrame(frame.Data, buffer.Span[position..]);
            if (position <= 1024)
                continue;

            yield return buffer.Span[..position].ToArray();
            position = 0;
        }

        int WriteFrame(byte[] frame, Span<byte> span)
        {
            var spanWriter = new SpanWriter(span);
            var sizeOfLength = spanWriter.WriteVInt((ulong)frame.Length);
            frame.CopyTo(span[sizeOfLength..]);
            return sizeOfLength + frame.Length;
        }
    }
}
