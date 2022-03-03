using System.Buffers;
using System.Buffers.Binary;

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
                // var buffered = 0;
                // var position = 0;
                var offsetMs = -1;
                var audioFrames = new List<AudioFrame>();
                var sequence = new ReadOnlySequence<byte>();
                // var pipeReader = PipeReader.Create(sequence);
                await foreach (var data in byteStream.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                    sequence = sequence.Append(data);
                    if (!formatTask.IsCompleted) {
                        if (sequence.Length < ActualOpusStreamHeader.Length + 1)
                            continue;

                        ReadFormat(ref sequence, ref formatTaskSource);
                    }

                    ReadFrames(ref sequence, audioFrames, ref offsetMs);
                    foreach (var audioFrame in audioFrames)
                        await target.Writer.WriteAsync(audioFrame, cancellationToken).ConfigureAwait(false);
                    audioFrames.Clear();

                    void ReadFormat(ref ReadOnlySequence<byte> sequence1, ref TaskSource<AudioFormat> formatTaskSource1)
                    {
                        Span<byte> buffer = stackalloc byte[ActualOpusStreamHeader.Length + 1];
                        sequence1.Slice(0, ActualOpusStreamHeader.Length + 1).CopyTo(buffer);
                        if (!buffer.StartsWith(ActualOpusStreamHeader))
                            throw new InvalidOperationException("Actual Opus stream header is invalid.");

                        var version = buffer[ActualOpusStreamHeader.Length];
                        if (version != 1)
                            throw new NotSupportedException($"Actual Opus stream version is invalid - ${version}. Only version 1 is supported.");

                        formatTaskSource1.SetResult(AudioSource.DefaultFormat);
                        sequence1 = sequence1.Slice(ActualOpusStreamHeader.Length + 1);
                    }

                    void ReadFrames(ref ReadOnlySequence<byte> sequence1, List<AudioFrame> frames1, ref int offsetMs1)
                    {
                        Span<byte> buffer = stackalloc byte[2];
                        while (true) {
                            if (sequence1.Length < 2)
                                return;

                            var sizeSequence = sequence1.Slice(0, 2);
                            ushort packetSize;
                            if (sizeSequence.IsSingleSegment)
                                packetSize = BinaryPrimitives.ReadUInt16LittleEndian(sizeSequence.FirstSpan);
                            else {
                                sizeSequence.CopyTo(buffer);
                                packetSize = BinaryPrimitives.ReadUInt16LittleEndian(buffer);
                            }
                            sequence1 = sequence1.Slice(2);
                            if (sequence1.Length < packetSize)
                                return;

                            var packetSequence = sequence1.Slice(0, packetSize);
                            var packet = packetSequence.ToArray();
                            offsetMs1 += 20; // 20-ms frames
                            if (offsetMs1 >= 0)
                                frames1.Add(new AudioFrame {
                                    Data = packet,
                                    Offset = TimeSpan.FromMilliseconds(offsetMs1),
                                });
                        }
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
            ushort length = (ushort)frame.Length;
            BinaryPrimitives.WriteUInt16LittleEndian(span, length);
            frame.CopyTo(span[2..]);
            return 2 + frame.Length;
        }
    }
}
