using System.Buffers;
using ActualChat.Audio.WebM;
using ActualChat.Audio.WebM.Models;

namespace ActualChat.Audio.UnitTests;

public class WebMReaderTest : TestBase
{
    public WebMReaderTest(ITestOutputHelper @out) : base(@out)
    { }

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
    public async Task ReaderSmallBufferTest()
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

        var entries = Parse(buffer1.Span[..bytesRead1], buffer2.Span[..bytesRead2]).ToList();
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

    private List<BaseModel> Parse(Span<byte> span1, Span<byte> span2)
    {
        var result = new List<BaseModel>();
        var reader = new WebMReader(span1);
        while (reader.Read())
            result.Add(reader.ReadResult);

        // using var bufferLease1 = MemoryPool<byte>.Shared.Rent(3 * 1024);
        var tailLength = reader.Tail.Length;
        var thereIsNoTail = reader.Tail.IsEmpty;
        var bufferLength = span2.Length;
        if (thereIsNoTail)
            reader = reader.WithNewSource(span2);
        else {
            var dataSize = span2.Length + tailLength;
            using var bufferLease = MemoryPool<byte>.Shared.Rent(dataSize);
            var span = bufferLease.Memory.Span;
            bufferLength = span.Length;
            Console.Out.WriteLine("Combined buffer has length = " + span.Length);
            reader.Tail.CopyTo(span);
            span2.CopyTo(span[tailLength..]);
            reader = reader.WithNewSource(span[..dataSize]);
        }
        try {
            while (reader.Read())
                result.Add(reader.ReadResult);
        }
        catch (Exception e) {
            throw new InvalidOperationException("Error reading WebM. Current buffer has length: " + bufferLength, e);
        }
        return result;
    }
}
