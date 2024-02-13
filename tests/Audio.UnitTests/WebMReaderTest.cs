using System.Buffers;
using ActualChat.Audio.WebM;
using ActualChat.Audio.WebM.Models;

namespace ActualChat.Audio.UnitTests;

public class WebMReaderTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public async Task BasicReaderTest()
    {
        await using var inputStream = new FileStream(
            Path.Combine(Environment.CurrentDirectory, "data", "file.webm"),
            FileMode.Open,
            FileAccess.Read);
        using var bufferLease = MemoryPool<byte>.Shared.Rent(3 * 1024);
        var buffer = bufferLease.Memory;
        var bytesRead = await inputStream.ReadAsync(buffer);
        while (bytesRead < 3 * 1024)
            bytesRead += await inputStream.ReadAsync(buffer[bytesRead..]);
        bytesRead.Should().BeGreaterThan(3 * 1024);

        var entries = Parse(buffer.Span[..bytesRead]).ToList();
        entries.Should().HaveCount(13);
        entries.Should().NotContainNulls();
        entries[0].Should().BeOfType<EBML>();
        entries[1].Should().BeOfType<Segment>();
        entries[2].Should().BeOfType<Cluster>();
        entries[2].As<Cluster>().SimpleBlocks.Should().HaveCount(10);
        entries[2].As<Cluster>().SimpleBlocks.Should().NotContainNulls();
        entries[3].Should().BeOfType<SimpleBlock>();
    }

    [Fact]
    public async Task BrokenWasmReaderTest()
    {
        await using var inputStream = new FileStream(
            Path.Combine(Environment.CurrentDirectory, "data", "file.webm"),
            FileMode.Open,
            FileAccess.Read);
        using var bufferLease = MemoryPool<byte>.Shared.Rent(4 * 1024);
        var buffer = bufferLease.Memory;
        var bytesRead = await inputStream.ReadAsync(buffer);
        while (bytesRead < 4 * 1024)
            bytesRead += await inputStream.ReadAsync(buffer[bytesRead..]);
        bytesRead.Should().BeGreaterThanOrEqualTo(4 * 1024);

        var entries = Parse(buffer[..25], buffer[25..bytesRead]).ToList();
        entries.Should().HaveCount(13);
        entries.Should().NotContainNulls();
        entries[0].Should().BeOfType<EBML>();
        entries[1].Should().BeOfType<Segment>();
        entries[2].Should().BeOfType<Cluster>();
        entries[2].As<Cluster>().SimpleBlocks.Should().HaveCount(10);
        entries[2].As<Cluster>().SimpleBlocks.Should().NotContainNulls();
        entries[3].Should().BeOfType<SimpleBlock>();
    }

    [Fact]
    public async Task Read1ByteBufferTest()
    {
        await using var inputStream = new FileStream(
            Path.Combine(Environment.CurrentDirectory, "data", "file.webm"),
            FileMode.Open,
            FileAccess.Read);
        using var bufferLease = MemoryPool<byte>.Shared.Rent(4 * 1024);
        var buffer = bufferLease.Memory;
        var bytesRead = await inputStream.ReadAsync(buffer);
        while (bytesRead < 4 * 1024)
            bytesRead += await inputStream.ReadAsync(buffer[bytesRead..]);
        bytesRead.Should().BeGreaterThanOrEqualTo(4 * 1024);

        var buffers = new Memory<byte>[buffer.Length];
        for (var i = 0; i < buffers.Length; i++)
            buffers[i] = buffer.Slice(i, 1);
        var entries = Parse(buffers).ToList();
        entries.Should().HaveCount(13);
        entries.Should().NotContainNulls();
        entries[0].Should().BeOfType<EBML>();
        entries[1].Should().BeOfType<Segment>();
        entries[2].Should().BeOfType<Cluster>();
        entries[2].As<Cluster>().SimpleBlocks.Should().HaveCount(10);
        entries[2].As<Cluster>().SimpleBlocks.Should().NotContainNulls();
        entries[3].Should().BeOfType<SimpleBlock>();
    }

    [Fact]
    public async Task ReadHeaderAndOneBlockTest()
    {
        await using var inputStream = new FileStream(
            Path.Combine(Environment.CurrentDirectory, "data", "file.webm"),
            FileMode.Open,
            FileAccess.Read);
        using var bufferLease = MemoryPool<byte>.Shared.Rent(3 * 1024);
        var buffer = bufferLease.Memory;
        var bytesRead = await inputStream.ReadAsync(buffer[..0x287]);
        while (bytesRead < 0x287)
            bytesRead += await inputStream.ReadAsync(buffer[bytesRead..0x287]);

        // WebMReader.Read doesn't return cluster, because it's not sure that the cluster is completed
        var entries = Parse(buffer.Span[..0x287]).ToList();
        entries.Should().HaveCount(4);
        entries.Should().NotContainNulls();
        entries[0].Should().BeOfType<EBML>();
        entries[1].Should().BeOfType<Segment>();
        entries[2].Should().BeOfType<Cluster>();
        entries[3].Should().BeOfType<SimpleBlock>();
    }

    [Fact]
    public async Task ReadOnlyHeaderTest()
    {
        await using var inputStream = new FileStream(
            Path.Combine(Environment.CurrentDirectory, "data", "file.webm"),
            FileMode.Open,
            FileAccess.Read);
        using var bufferLease = MemoryPool<byte>.Shared.Rent(3 * 1024);
        var buffer = bufferLease.Memory;
        var bytesRead = await inputStream.ReadAsync(buffer[..0xA1]);
        while (bytesRead < 0xA1)
            bytesRead += await inputStream.ReadAsync(buffer[bytesRead..0xA1]);

        // WebMReader.Read doesn't return cluster, because it's not sure that the cluster is completed
        var entries = Parse(buffer.Span[..0xA1]).ToList();
        entries.Should().HaveCount(2);
        entries.Should().NotContainNulls();
        entries[0].Should().BeOfType<EBML>();
        entries[1].Should().BeOfType<Segment>();
    }

    [Fact(Skip = "Not yet fixed")]
    public async Task ReadRemuxedWebMTest()
    {
        await using var inputStream = new FileStream(
            Path.Combine(Environment.CurrentDirectory, "data", "0001.webm"),
            FileMode.Open,
            FileAccess.Read);
        using var bufferLease = MemoryPool<byte>.Shared.Rent(50 * 1024);
        var buffer = bufferLease.Memory;
        var bytesRead = await inputStream.ReadAsync(buffer);
        while (bytesRead < 50 * 1024)
            bytesRead += await inputStream.ReadAsync(buffer[bytesRead..]);
        bytesRead.Should().BeGreaterOrEqualTo(50 * 1024);

        var entries = Parse(buffer.Span[..bytesRead]).ToList();
        entries.Should().HaveCount(4);
        entries.Should().NotContainNulls();
        entries[0].Should().BeOfType<EBML>();
        entries[1].Should().BeOfType<Segment>();
        entries[2].Should().BeOfType<Cluster>();
        entries[3].Should().BeOfType<SimpleBlock>();
    }

    [Fact]
    public async Task ReadSmallBufferTest()
    {
        await using var inputStream = new FileStream(
            Path.Combine(Environment.CurrentDirectory, "data", "file.webm"),
            FileMode.Open,
            FileAccess.Read);
        using var bufferLease = MemoryPool<byte>.Shared.Rent(3 * 1024);
        var buffer = bufferLease.Memory;
        var bytesRead = await inputStream.ReadAsync(buffer[..0x26]);
        while (bytesRead < 0x26)
            bytesRead += await inputStream.ReadAsync(buffer[bytesRead..0x26]);

        var entries = Parse(buffer.Span[..0x26]).ToList();
        entries.Should().HaveCount(1);
        entries.Should().NotContainNulls();
        entries[0].Should().BeOfType<EBML>();
    }

    [Fact]
    public async Task SequentialBlockReaderTest()
    {
        await using var inputStream = new FileStream(
            Path.Combine(Environment.CurrentDirectory, "data", "file.webm"),
            FileMode.Open,
            FileAccess.Read);
        using var bufferLease1 = MemoryPool<byte>.Shared.Rent(3 * 1024);
        using var bufferLease2 = MemoryPool<byte>.Shared.Rent(3 * 1024);
        var buffer1 = bufferLease1.Memory;
        var buffer2 = bufferLease2.Memory;
        var bytesRead1 = await inputStream.ReadAsync(buffer1);
        var bytesRead2 = await inputStream.ReadAsync(buffer2);

        while (bytesRead1 < 3 * 1024)
            bytesRead1 += await inputStream.ReadAsync(buffer1[bytesRead1..]);
        while (bytesRead2 < 3 * 1024)
            bytesRead2 += await inputStream.ReadAsync(buffer2[bytesRead2..]);
        bytesRead1.Should().BeGreaterThan(3 * 1024);
        bytesRead2.Should().BeGreaterThan(3 * 1024);

        var entries = Parse(buffer1[..bytesRead1], buffer2[..bytesRead2]).ToList();
        entries.Should().HaveCount(23);
        entries.Should().NotContainNulls();
        entries[0].Should().BeOfType<EBML>();
        entries[1].Should().BeOfType<Segment>();
        entries[2].Should().BeOfType<Cluster>();
        entries[2].As<Cluster>().SimpleBlocks.Should().HaveCount(20);
        entries[2].As<Cluster>().SimpleBlocks.Should().NotContainNulls();
    }

    private List<BaseModel> Parse(Span<byte> span)
    {
        var result = new List<BaseModel>();
        var reader = new WebMReader(span);
        while (reader.Read())
            result.Add(reader.ReadResult);
        return result;
    }

    private List<BaseModel> Parse(params Memory<byte>[] dataElements)
    {
        var result = new List<BaseModel>();
        var state = new WebMReader.State();
        var readBufferLease = MemoryPool<byte>.Shared.Rent(32 * 1024); // Disposed in the last "finally"
        var readBuffer = readBufferLease.Memory;

        foreach (var data in dataElements) {
            var remainingLength = state.Remaining;
            readBuffer.Span.Slice(state.Position, remainingLength)
                .CopyTo(readBuffer.Span[..remainingLength]);
            data.CopyTo(readBuffer[state.Remaining..]);
            var dataLength = state.Remaining + data.Length;

            var webMReader = state.IsEmpty
                ? new WebMReader(readBuffer.Span[..dataLength])
                : WebMReader.FromState(state).WithNewSource(readBufferLease.Memory.Span[..dataLength]);

            try {
                while (webMReader.Read())
                    result.Add(webMReader.ReadResult);
            }
            catch (Exception e) {
                throw new InvalidOperationException("Error reading WebM. DataLength: " + dataLength, e);
            }
            state = webMReader.GetState();
        }

        return result;
    }
}
