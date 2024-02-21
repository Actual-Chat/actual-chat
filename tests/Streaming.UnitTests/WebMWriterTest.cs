using System.Buffers;
using ActualChat.Audio.WebM;
using ActualChat.Audio.WebM.Models;

namespace ActualChat.Streaming.UnitTests;

public class WebMWriterTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public async Task BasicWriterTest()
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

        var (entry1, state1) = Parse(new WebMReader(buffer.Span[..bytesRead]));
        var (entry2, state2) =
            Parse(WebMReader.FromState(state1).WithNewSource(buffer.Span[state1.Position..bytesRead]));
        var (entry3, _) = Parse(
            WebMReader.FromState(state2).WithNewSource(buffer.Span[(state2.Position + state1.Position)..bytesRead]));

        using var writeBufferLease = MemoryPool<byte>.Shared.Rent(3 * 1024);
        var writeBuffer = writeBufferLease.Memory;
        var (ebmlWritten, position1) = Write(new WebMWriter(writeBuffer.Span), entry1);
        ebmlWritten.Should().BeTrue();

        var (segmentWritten, position2) = Write(new WebMWriter(writeBuffer.Span[position1..]), entry2);
        segmentWritten.Should().BeTrue();

        var (clusterWritten, position3) = Write(new WebMWriter(writeBuffer.Span[(position1 + position2)..]), entry3);
        clusterWritten.Should().BeTrue();

        (BaseModel, WebMReader.State) Parse(WebMReader reader)
        {
            reader.Read();
            return (reader.ReadResult, reader.GetState());
        }

        (bool success, int position) Write(WebMWriter writer, BaseModel entry)
        {
            var success = writer.Write(entry);
            return (success, writer.Position);
        }
    }

    [Fact]
    public async Task ReadWriteMatchTest()
    {
        await using var inputStream = new FileStream(
            Path.Combine(Environment.CurrentDirectory, "data", "file.webm"),
            FileMode.Open,
            FileAccess.Read);
        using var readBufferLease = MemoryPool<byte>.Shared.Rent(10 * 1024);
        using var writeBufferLease = MemoryPool<byte>.Shared.Rent(10 * 1024);
        var readBuffer = readBufferLease.Memory;
        var writeBuffer = writeBufferLease.Memory;
        WebMReader.State currentState = default;
        var bytesRead = await inputStream.ReadAsync(readBuffer[currentState.Position..]);
        while (bytesRead < 10 * 1024)
            bytesRead += await inputStream.ReadAsync(readBuffer[bytesRead..]);

        while (bytesRead > 0) {
            var (elements, state) = Parse(currentState.IsEmpty
                ? new WebMReader(readBuffer.Span[..bytesRead])
                : WebMReader.FromState(currentState)
                    .WithNewSource(readBuffer.Span[..(currentState.Remaining + bytesRead)]));
            currentState = state;

            var endPosition = Write(new WebMWriter(writeBuffer.Span), elements);

            AssertBuffersAreSame(readBuffer.Span, writeBuffer.Span, endPosition);

            readBuffer.Slice(currentState.Position, currentState.Remaining)
                .CopyTo(readBuffer[..currentState.Remaining]);

            var read = 0;
            var readInternal = 0;
            do {
                readInternal = await inputStream.ReadAsync(readBuffer[(currentState.Remaining + read)..]);
                read += readInternal;
            } while (readInternal > 0 && read < 10 * 1024);
            bytesRead = read;
        }

        (IReadOnlyList<BaseModel>, WebMReader.State) Parse(WebMReader reader)
        {
            var result = new List<BaseModel>();
            while (reader.Read())
                result.Add(reader.ReadResult);
            return (result, reader.GetState());
        }

        int Write(WebMWriter writer, IReadOnlyList<BaseModel> elements)
        {
            foreach (var element in elements) {
                var success = writer.Write(element);
                success.Should().BeTrue();
            }
            return writer.Position;
        }

        void AssertBuffersAreSame(ReadOnlySpan<byte> read, ReadOnlySpan<byte> written, int endPosition)
        {
            var readSpan = read[..endPosition];
            var writtenSpan = written[..endPosition];
            for (var i = 0; i < endPosition; i++)
                readSpan[i].Should().Be(writtenSpan[i], "should match at Index {0}", i);
        }
    }

    [Fact(Skip = "Run it manually to produce file-out.webm")]
    public async Task ReadWriteToFileTest()
    {
        await using var inputStream = new FileStream(
            Path.Combine(Environment.CurrentDirectory, "data", "file.webm"),
            FileMode.Open,
            FileAccess.Read);
        await using var outputStream = new FileStream(
            Path.Combine(Environment.CurrentDirectory, "data", "file-out.webm"),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite);
        using var readBufferLease = MemoryPool<byte>.Shared.Rent(10 * 1024);
        using var writeBufferLease = MemoryPool<byte>.Shared.Rent(100 * 1024);
        var readBuffer = readBufferLease.Memory;
        var writeBuffer = writeBufferLease.Memory;
        WebMReader.State currentState = default;
        var bytesRead = await inputStream.ReadAsync(readBuffer[currentState.Position..]);
        var endPosition = 0;
        while (bytesRead > 0) {
            var (elements, state) = Parse(currentState.IsEmpty
                ? new WebMReader(readBuffer.Span[..bytesRead])
                : WebMReader.FromState(currentState)
                    .WithNewSource(readBuffer.Span[..(currentState.Remaining + bytesRead)]));
            currentState = state;

            endPosition = Write(new WebMWriter(writeBuffer.Span), elements);

            await outputStream.WriteAsync(writeBuffer[..endPosition]);

            readBuffer.Slice(currentState.Position, currentState.Remaining)
                .CopyTo(readBuffer[..currentState.Remaining]);

            bytesRead = await inputStream.ReadAsync(readBuffer[currentState.Remaining..]);
        }
        endPosition = Write(new WebMWriter(writeBuffer.Span), new[] { (RootEntry)currentState.Entry! });
        await outputStream.WriteAsync(writeBuffer[..endPosition]);

        await outputStream.FlushAsync();

        (IReadOnlyList<BaseModel>, WebMReader.State) Parse(WebMReader reader)
        {
            var result = new List<BaseModel>();
            while (reader.Read())
                result.Add(reader.ReadResult);
            return (result, reader.GetState());
        }

        int Write(WebMWriter writer, IReadOnlyList<BaseModel> elements)
        {
            foreach (var element in elements) {
                var success = writer.Write(element);
                success.Should().BeTrue();
            }
            return writer.Position;
        }
    }
}
