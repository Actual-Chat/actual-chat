using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using ActualLab.IO;

namespace ActualChat.ContentCaching.UnitTests;

public sealed class EncryptedContentFileTest : IDisposable
{
    private readonly FilePath _directory = TestDirectory.New();
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    private FilePath Path => _directory & "content";

    public EncryptedContentFileTest()
        => Directory.CreateDirectory(_directory);

    public void Dispose()
        => Directory.Delete(_directory, true);

    [Fact]
    public async Task ReadersShouldSeeEachAppendBeforeCompletion()
    {
        // arrange
        using var file = await EncryptedContentFile.Create(Path, "asset", _key, "metadata"u8.ToArray(), default);
        using var first = file.CreateReader();
        using var second = file.CreateReader();
        var buffer = new byte[32];

        // act
        await file.Append("abc"u8.ToArray(), default);
        var firstCount = await first.ReadAsync(buffer);
        var firstText = Encoding.UTF8.GetString(buffer, 0, firstCount);
        var atEnd = await first.ReadAsync(buffer);
        await file.Append("defgh"u8.ToArray(), default);
        var secondCount = await first.ReadAsync(buffer);
        var secondText = Encoding.UTF8.GetString(buffer, 0, secondCount);
        using var all = new MemoryStream();
        await second.CopyToAsync(all);

        // assert
        firstText.Should().Be("abc");
        atEnd.Should().Be(0);
        secondText.Should().Be("defgh");
        all.ToArray().Should().Equal("abcdefgh"u8.ToArray());
        file.Length.Should().Be(8);
    }

    [Fact]
    public async Task CompletedFileShouldRestartAndSeekAcrossArbitraryAppends()
    {
        // arrange
        var body = RandomNumberGenerator.GetBytes(150_013);
        using (var file = await EncryptedContentFile.Create(
                   Path, "asset", _key, "secret metadata"u8.ToArray(), default)) {
            var offset = 0;
            foreach (var size in new[] { 1, 7, 65536, 23, 65536, 18910 }) {
                await file.Append(body.AsMemory(offset, size), default);
                offset += size;
            }
            await file.Complete(default);
        }

        // act
        using var reopened = await EncryptedContentFile.Open(Path, "asset", _key, default);
        using var first = reopened.CreateReader();
        using var second = reopened.CreateReader();
        first.Seek(65540, SeekOrigin.Begin);
        var middle = new byte[100];
        await first.ReadExactlyAsync(middle);
        first.Seek(-3, SeekOrigin.End);
        var tail = new byte[3];
        await first.ReadExactlyAsync(tail);
        first.Position = 1;
        var head = first.ReadByte();
        using var result = new MemoryStream();
        await second.CopyToAsync(result);
        var disk = await File.ReadAllBytesAsync(Path);

        // assert
        reopened.Metadata.Should().Equal("secret metadata"u8.ToArray());
        reopened.Length.Should().Be(body.Length);
        middle.Should().Equal(body[65540..65640]);
        tail.Should().Equal(body[^3..]);
        head.Should().Be(body[1]);
        result.ToArray().Should().Equal(body);
        disk.AsSpan().IndexOf(body.AsSpan(0, 64)).Should().Be(-1);
        Encoding.UTF8.GetString(disk).Should().NotContain("secret metadata");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WrongKeyOrIdentityShouldRejectOpen(bool hasWrongKey)
    {
        // arrange
        await WriteCompleted();
        var key = hasWrongKey ? RandomNumberGenerator.GetBytes(32) : _key;
        var identity = hasWrongKey ? "asset" : "another asset";

        // act
        var openTask = () => EncryptedContentFile.Open(Path, identity, key, default);

        // assert
        await openTask.Should().ThrowAsync<CryptographicException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(30)]
    public async Task TruncatedOrTamperedFooterShouldRejectOpen(int truncateCount)
    {
        // arrange
        await WriteCompleted();
        var bytes = await File.ReadAllBytesAsync(Path);
        if (truncateCount == 0)
            bytes[^1] ^= 1;
        else
            bytes = bytes[..^truncateCount];
        await File.WriteAllBytesAsync(Path, bytes);

        // act
        var openTask = () => EncryptedContentFile.Open(Path, "asset", _key, default);

        // assert
        if (truncateCount == 0)
            await openTask.Should().ThrowAsync<CryptographicException>();
        else
            await openTask.Should().ThrowAsync<InvalidDataException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TamperedOrReorderedRecordsShouldFailBeforeReturningTheirBytes(bool mustReorder)
    {
        // arrange
        long firstOffset;
        long secondOffset;
        long thirdOffset;
        using (var file = await EncryptedContentFile.Create(Path, "asset", _key, [], default)) {
            firstOffset = new FileInfo(Path).Length;
            await file.Append("first"u8.ToArray(), default);
            secondOffset = new FileInfo(Path).Length;
            await file.Append("other"u8.ToArray(), default);
            thirdOffset = new FileInfo(Path).Length;
            await file.Append("third"u8.ToArray(), default);
            await file.Complete(default);
        }
        var bytes = await File.ReadAllBytesAsync(Path);
        if (mustReorder) {
            var first = bytes[(int)firstOffset..(int)secondOffset];
            bytes.AsSpan((int)secondOffset, first.Length).CopyTo(bytes.AsSpan((int)firstOffset));
            first.CopyTo(bytes, (int)secondOffset);
        }
        else
            bytes[(int)thirdOffset - 1] ^= 1;
        await File.WriteAllBytesAsync(Path, bytes);
        using var reopened = await EncryptedContentFile.Open(Path, "asset", _key, default);
        using var reader = reopened.CreateReader();
        var result = new byte[5];
        if (!mustReorder)
            await reader.ReadExactlyAsync(result);
        Array.Fill(result, (byte)0);

        // act
        var readTask = async () => await reader.ReadAsync(result);

        // assert
        if (mustReorder)
            await readTask.Should().ThrowAsync<InvalidDataException>();
        else
            await readTask.Should().ThrowAsync<CryptographicException>();
        result.Should().OnlyContain(value => value == 0);
    }

    [Fact]
    public async Task ReadersShouldRetainTheHandleUntilDisposed()
    {
        // arrange
        var file = await EncryptedContentFile.Create(Path, "asset", _key, [], default);
        await file.Append("body"u8.ToArray(), default);
        using var reader = file.CreateReader();

        // act
        file.Dispose();
        File.Move(Path, Path + ".moved");
        using var result = new MemoryStream();
        await reader.CopyToAsync(result);

        // assert
        result.ToArray().Should().Equal("body"u8.ToArray());
        var createReader = () => file.CreateReader();
        createReader.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task SimultaneousReadersShouldFollowOneWriter()
    {
        // arrange
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var body = RandomNumberGenerator.GetBytes(100_000);
        using var file = await EncryptedContentFile.Create(Path, "asset", _key, [], timeout.Token);
        using var first = file.CreateReader();
        using var second = file.CreateReader();
        var firstTask = Task.Run(() => ReadAll(first), timeout.Token);
        var secondTask = Task.Run(() => ReadAll(second), timeout.Token);

        // act
        for (var offset = 0; offset < body.Length; offset += 997)
            await file.Append(body.AsMemory(offset, Math.Min(997, body.Length - offset)), timeout.Token);
        await file.Complete(timeout.Token);
        var results = await Task.WhenAll(firstTask, secondTask);

        // assert
        results[0].Should().Equal(body);
        results[1].Should().Equal(body);
        return;

        async Task<byte[]> ReadAll(Stream reader) {
            var bytes = new byte[body.Length];
            var offset = 0;
            while (offset < bytes.Length) {
                var count = await reader.ReadAsync(bytes.AsMemory(offset), timeout.Token);
                offset += count;
                if (count == 0)
                    await Task.Yield();
            }
            return bytes;
        }
    }

    [Fact]
    public async Task EmptyCompletedFileShouldRestartWithMaximumMetadata()
    {
        // arrange
        var metadata = RandomNumberGenerator.GetBytes(65536);
        using (var file = await EncryptedContentFile.Create(Path, "asset", _key, metadata, default))
            await file.Complete(default);

        // act
        using var reopened = await EncryptedContentFile.Open(Path, "asset", _key, default);
        using var reader = reopened.CreateReader();
        var count = reader.ReadByte();

        // assert
        reopened.Metadata.Should().Equal(metadata);
        reopened.Length.Should().Be(0);
        count.Should().Be(-1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65537)]
    public async Task InvalidAppendShouldNotChangePublishedContent(int length)
    {
        // arrange
        using var file = await EncryptedContentFile.Create(Path, "asset", _key, [], default);
        await file.Append("first"u8.ToArray(), default);

        // act
        var appendTask = () => file.Append(new byte[length], default);

        // assert
        await appendTask.Should().ThrowAsync<ArgumentOutOfRangeException>();
        file.Length.Should().Be(5);
        await file.Complete(default);
    }

    [Fact]
    public async Task IncompleteFileShouldNotOpen()
    {
        // arrange
        using var file = await EncryptedContentFile.Create(Path, "asset", _key, [], default);
        await file.Append(new byte[65536], default);

        // act
        var openTask = () => EncryptedContentFile.Open(Path, "asset", _key, default);

        // assert
        await openTask.Should().ThrowAsync<InvalidDataException>();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public async Task UntrustedMetadataLengthsShouldBeRejectedBeforeAllocation(int length)
    {
        // arrange
        await WriteCompleted();
        var bytes = await File.ReadAllBytesAsync(Path);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(40), length);
        await File.WriteAllBytesAsync(Path, bytes);

        // act
        var openTask = () => EncryptedContentFile.Open(Path, "asset", _key, default);

        // assert
        await openTask.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task RecordsFromAnotherGenerationShouldFailAuthentication()
    {
        // arrange
        var anotherPath = _directory & "another";
        long dataStart;
        long dataEnd;
        using (var file = await EncryptedContentFile.Create(Path, "asset", _key, [], default)) {
            dataStart = new FileInfo(Path).Length;
            await file.Append("first"u8.ToArray(), default);
            dataEnd = new FileInfo(Path).Length;
            await file.Complete(default);
        }
        using (var file = await EncryptedContentFile.Create(anotherPath, "asset", _key, [], default)) {
            await file.Append("other"u8.ToArray(), default);
            await file.Complete(default);
        }
        var bytes = await File.ReadAllBytesAsync(Path);
        var another = await File.ReadAllBytesAsync(anotherPath);
        another.AsSpan((int)dataStart, (int)(dataEnd - dataStart)).CopyTo(bytes.AsSpan((int)dataStart));
        await File.WriteAllBytesAsync(Path, bytes);
        using var reopened = await EncryptedContentFile.Open(Path, "asset", _key, default);
        using var reader = reopened.CreateReader();

        // act
        var read = () => reader.ReadByte();

        // assert
        read.Should().Throw<CryptographicException>();
    }

    [Fact]
    public async Task CancellationDuringSeekScanShouldPreserveReaderPosition()
    {
        // arrange
        using (var file = await EncryptedContentFile.Create(Path, "asset", _key, [], default)) {
            for (var i = 0; i < 4096; i++)
                await file.Append(new byte[] { 7 }, default);
            await file.Complete(default);
        }
        using var reopened = await EncryptedContentFile.Open(Path, "asset", _key, default);
        using var reader = reopened.CreateReader();
        using var cancellation = new CancellationTokenSource();
        reader.Seek(4095, SeekOrigin.Begin);
        var buffer = new byte[1];

        // act
        var readTask = reader.ReadAsync(buffer, cancellation.Token).AsTask();
        cancellation.Cancel();
        var read = async () => await readTask;

        // assert
        await read.Should().ThrowAsync<OperationCanceledException>();
        buffer.Should().Equal(new byte[] { 0 });
        reader.Position.Should().Be(4095);
        (await reader.ReadAsync(buffer)).Should().Be(1);
        buffer.Should().Equal(new byte[] { 7 });
    }
    // Private methods

    private async Task WriteCompleted()
    {
        using var file = await EncryptedContentFile.Create(Path, "asset", _key, [], default);
        await file.Append("private body content"u8.ToArray(), default);
        await file.Complete(default);
    }
}
