using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ActualChat.Audio.WebM;
using ActualChat.Audio.WebM.Models;
using FluentAssertions;
using Stl.Testing;
using Xunit;
using Xunit.Abstractions;

namespace ActualChat.Tests.Audio
{
    public class WebMReaderTest : TestBase
    {
        public WebMReaderTest(ITestOutputHelper @out) : base(@out)
        {
        }

        [Fact]
        public async Task BasicReaderTest()
        {
            await using var inputStream = new FileStream(Path.Combine(Environment.CurrentDirectory, "data", "file.webm"), FileMode.Open, FileAccess.Read);
            using var bufferLease = MemoryPool<byte>.Shared.Rent(3 * 1024);
            var buffer = bufferLease.Memory;
            var bytesRead = await inputStream.ReadAsync(buffer);

            bytesRead.Should().BeGreaterThan(3 * 1024);

            var entries = Parse(buffer.Span[..bytesRead]).ToList();
            entries.Should().HaveCount(3);
            entries.Should().NotContainNulls();
            entries[0].Should().BeOfType<EBML>();
            entries[1].Should().BeOfType<Segment>();
            entries[2].Should().BeOfType<Cluster>();
            entries[2].As<Cluster>().SimpleBlocks.Should().HaveCount(10);
            entries[2].As<Cluster>().SimpleBlocks.Should().NotContainNulls();
        }

        [Fact]
        public async Task ReaderSmallBufferTest()
        {
            await using var inputStream = new FileStream(Path.Combine(Environment.CurrentDirectory, "data", "file.webm"), FileMode.Open, FileAccess.Read);
            using var bufferLease = MemoryPool<byte>.Shared.Rent(3 * 1024);
            var buffer = bufferLease.Memory;
            var bytesRead = await inputStream.ReadAsync(buffer[..0x26]);

            var entries = Parse(buffer.Span[..0x26]).ToList();
            entries.Should().HaveCount(2);
            entries.Should().NotContainNulls();
            entries[0].Should().BeOfType<EBML>();
            entries[1].Should().BeOfType<EBML>();
        }


        [Fact]
        public async Task SequentalBlockReaderTest()
        {
            await using var inputStream = new FileStream(Path.Combine(Environment.CurrentDirectory, "data", "file.webm"), FileMode.Open, FileAccess.Read);
            using var bufferLease1 = MemoryPool<byte>.Shared.Rent(3 * 1024);
            using var bufferLease2 = MemoryPool<byte>.Shared.Rent(3 * 1024);
            var buffer1 = bufferLease1.Memory;
            var buffer2 = bufferLease2.Memory;
            var bytesRead1 = await inputStream.ReadAsync(buffer1);
            var bytesRead2 = await inputStream.ReadAsync(buffer2);

            bytesRead1.Should().BeGreaterThan(3 * 1024);
            bytesRead2.Should().BeGreaterThan(3 * 1024);

            var entries = Parse(buffer1.Span, buffer2.Span).ToList();
            entries.Should().HaveCount(3);
            entries.Should().NotContainNulls();
            entries[0].Should().BeOfType<EBML>();
            entries[1].Should().BeOfType<Segment>();
            entries[2].Should().BeOfType<Cluster>();
            entries[2].As<Cluster>().SimpleBlocks.Should().HaveCount(21);
            entries[2].As<Cluster>().SimpleBlocks.Should().NotContainNulls();
        }

        private List<BaseModel> Parse(Span<byte> span)
        {
            var result = new List<BaseModel>();
            var reader = new WebMReader(span);
            while (reader.Read())
                result.Add(reader.Entry);
            result.Add(reader.Entry);
            return result;
        }

        private List<BaseModel> Parse(Span<byte> span1, Span<byte> span2)
        {
            var result = new List<BaseModel>();
            var reader = new WebMReader(span1);
            while (reader.Read())
                result.Add(reader.Entry);

            // using var bufferLease1 = MemoryPool<byte>.Shared.Rent(3 * 1024);
            if (!reader.Tail.IsEmpty) {
                using var bufferLease = MemoryPool<byte>.Shared.Rent(span2.Length + reader.Tail.Length);
                var span = bufferLease.Memory.Span;
                reader.Tail.CopyTo(span);
                span2.CopyTo(span[reader.Tail.Length..]);
                reader = reader.WithNewSource(span);
            }
            else
                reader = reader.WithNewSource(span2);
            while (reader.Read())
                result.Add(reader.Entry);
            result.Add(reader.Entry);
            return result;
        }
    }
}
