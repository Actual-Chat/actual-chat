using System.Buffers;
using System.Buffers.Binary;
using ActualChat.Spans;

namespace ActualChat.Audio;

public class ActualOpusStreamAdapter : IAudioStreamAdapter
{
    private static readonly byte[] ActualOpusStreamHeader = { 0x41, 0x5F, 0x4F, 0x50, 0x55, 0x53, 0x5F, 0x53 }; // A_OPUS_S
    private static readonly byte[] ActualOpusStreamFormat = { 0x41, 0x5F, 0x4F, 0x50, 0x55, 0x53, 0x5F, 0x53, 0x02 }; // A_OPUS_S + version = 2
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
                var offsetMs = 0;
                var audioFrames = new List<AudioFrame>();
                var sequence = new ReadOnlySequence<byte>();
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

                    static void ReadFormat(ref ReadOnlySequence<byte> sequence, ref TaskSource<AudioFormat> formatTaskSource)
                    {
                        Span<byte> buffer = stackalloc byte[ActualOpusStreamHeader.Length + 3];
                        sequence.Slice(0, ActualOpusStreamHeader.Length + 3).CopyTo(buffer);
                        if (!buffer.StartsWith(ActualOpusStreamHeader))
                            throw new InvalidOperationException("Actual Opus stream header is invalid.");

                        var version = buffer[ActualOpusStreamHeader.Length];
                        if (version is <= 0 or > 2)
                            throw new NotSupportedException($"Actual Opus stream version is invalid - ${version}. Only version 1-2 is supported.");

                        var format = AudioSource.DefaultFormat;
                        if (version == 2) {
                            var reader = new SpanReader(buffer[(ActualOpusStreamHeader.Length + 1)..]);
                            var skip = reader.ReadInt16();
                            if (!skip.HasValue)
                                throw new InvalidOperationException("Unable to read PreSkipFrames.");

                            format = format with { PreSkipFrames = skip.Value };
                            sequence = sequence.Slice(ActualOpusStreamHeader.Length + 3);
                        }
                        else
                            sequence = sequence.Slice(ActualOpusStreamHeader.Length + 1);
                        formatTaskSource.SetResult(format);
                    }

                    static void ReadFrames(ref ReadOnlySequence<byte> sequence, List<AudioFrame> frames1, ref int offsetMs)
                    {
                        const int uShortSize = sizeof(ushort);
                        Span<byte> buffer = stackalloc byte[uShortSize];
                        while (true) {
                            if (sequence.Length < uShortSize)
                                return;

                            var sizeSequence = sequence.Slice(0, uShortSize);
                            ushort packetSize;
                            if (sizeSequence.IsSingleSegment)
                                packetSize = BinaryPrimitives.ReadUInt16BigEndian(sizeSequence.FirstSpan);
                            else {
                                sizeSequence.CopyTo(buffer);
                                packetSize = BinaryPrimitives.ReadUInt16BigEndian(buffer);
                            }
                            if (sequence.Length < packetSize + uShortSize)
                                return;

                            sequence = sequence.Slice(uShortSize);
                            var packetSequence = sequence.Slice(0, packetSize);
                            var packet = packetSequence.ToArray();
                            sequence = sequence.Slice(packetSize);
                            offsetMs += 20; // 20-ms frames
                            if (offsetMs >= 0)
                                frames1.Add(new AudioFrame {
                                    Data = packet,
                                    Offset = TimeSpan.FromMilliseconds(offsetMs),
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
        await source.WhenFormatAvailable.ConfigureAwait(false);
        yield return WriteHeader(source);

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
            BinaryPrimitives.WriteUInt16BigEndian(span, length);
            frame.CopyTo(span[2..]);
            return 2 + frame.Length;
        }

        byte[] WriteHeader(AudioSource audioSource)
        {
            var header = new byte[ActualOpusStreamFormat.Length + 2];
            var spanWriter = new SpanWriter(header);
            spanWriter.Write(ActualOpusStreamFormat, ActualOpusStreamFormat.Length);
            spanWriter.Write((ushort)audioSource.Format.PreSkipFrames);
            return header;
        }
    }
}
