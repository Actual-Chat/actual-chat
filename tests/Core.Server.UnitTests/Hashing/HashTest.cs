using System.Text;
using ActualChat.Hashing;
using MemoryPack;
using Microsoft.Toolkit.HighPerformance;

namespace ActualChat.Core.Server.UnitTests.Hashing;

public class HashTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public unsafe void StructureTest()
    {
        sizeof(HashOutput16).Should().Be(16);
        sizeof(HashOutput20).Should().Be(20);
        sizeof(HashOutput32).Should().Be(32);
    }

    [Fact]
    public void Djb2_Test()
        => "".GetDjb2HashCode();

    [Fact]
    public Task MD5_Test()
        => Test(bytes => bytes.Hash().MD5(), stream => stream.Hash().MD5());

    [Fact]
    public Task SHA1_Test()
        => Test(bytes => bytes.Hash().SHA1(), stream => stream.Hash().SHA1());

    [Fact]
    public Task SHA256_Test()
        => Test(bytes => bytes.Hash().SHA256(), stream => stream.Hash().SHA256());

    [Fact]
    public void SHA256OfBaseClass_Test()
    {
        var obj = new HashedObject {
            Id = "1",
            Name = "Obj 1",
        };
        var obj2 = new HashedObject {
            Id = "1",
        };
        var hash = Sha256(obj);
        var hash2 = Sha256(obj2);
        var baseHash = Sha256<HashedObjectBase>(obj);
        var baseHash2 = Sha256<HashedObjectBase>(obj2);
        hash.Should().NotBe(baseHash);
        hash.Should().NotBe(hash2);
        baseHash.Should().Be(baseHash2);
        return;

        string Sha256<T>(T hashedObject)
        {
            using var buffer = ByteSerializer<T>.Default.Write(hashedObject);
            return buffer.WrittenSpan.Hash().SHA256().Base16();
        }
    }

    [Fact]
    public Task Blake2s_Test()
        => Test(bytes => bytes.Hash().Blake2s(), stream => stream.Hash().Blake2s());

    [Fact]
    public Task Blake2b_Test()
        => Test(bytes => bytes.Hash().Blake2b(), stream => stream.Hash().Blake2b());

    [Fact]
    public Task Blake3_Test()
        => Test(bytes => bytes.Hash().Blake3(), stream => stream.Hash().Blake3());

    private async Task Test<THash>(Func<byte[], THash> syncHasher, Func<Stream, Task<THash>> asyncHasher)
        where THash : struct, IHashOutput
    {
        for (var size = 0; size < 16; size++) {
            var buffer = new byte[size];
            Random.Shared.NextBytes(buffer);
            var h1 = syncHasher.Invoke(buffer);
            Out.WriteLine($"'{Convert.ToHexString(buffer)}' -> {h1}");
            h1.ToString().Should().Be(h1.Base16());
            h1.AsSpan<byte>().ToArray().Should().Equal(h1.Bytes.ToArray());
            h1.Base16(8).Should().Be(h1.Base16()[..16]);
            h1.Count<byte>().Should().Be(THash.Size);
            h1.Count<int>().Should().Be(THash.Size / 4);
            h1.Item<uint>(0).Should().Be(h1.First4Bytes);
            h1.Item<ulong>(0).Should().Be(h1.First8Bytes);
            h1.Item<Int128>(0).Should().Be(h1.First16Bytes);

            using var stream = new MemoryStream();
            await stream.WriteAsync(buffer);
            stream.Position = 0;
            var h2 = await asyncHasher.Invoke(stream);

            h2.Should().Be(h1);
            h2.ToString().Should().Be(h1.ToString());
            h2.First4Bytes.Should().Be(h1.First4Bytes);
            h2.First8Bytes.Should().Be(h1.First8Bytes);
            h2.First16Bytes.Should().Be(h1.First16Bytes);
            h2.AsSpan<byte>().ToArray().Should().Equal(h1.AsSpan<byte>().ToArray());
        }

        StringTest();
        return;

        void StringTest() {
            var encodings = new[] { null, Encoding.UTF8 };
            for (var iteration = 0; iteration < 3; iteration++) {
                var source = Alphabet.Base64.Generator16.Next();
                for (var size = 0; size < source.Length; size++) {
                    var s = source[..size];
                    var hashes = encodings.Select(e => {
                        var hash = syncHasher.Invoke(s.Encode(e).ToArray());
                        return $"{hash.First4Bytes:x8}";
                    }).ToArray();
                    Out.WriteLine($"  '{s}' -> {hashes.ToDelimitedString(" ")}");
                }
            }
        }
    }

    [Fact]
    public void XorStressTest()
    {
        // arrange
        var hashes = Enumerable.Range(1, 1_000_000)
            .Select(BitConverter.GetBytes)
            .Select(b => b.Hash().SHA256())
            .ToList();

        // act
        var result = hashes.BitwiseXor().Base64();
        var result2 = hashes.BitwiseXor().Base64();

        // assert
        Out.WriteLine(result);
        result.Should().NotBeEmpty().And.Be(result2);
    }

    [Fact]
    public void XorUniquenessStressTest()
    {
        // arrange
        var uniqueXorResults = new HashSet<string>();
        var hashes = Enumerable.Range(1, 1_000_000)
            .Select(BitConverter.GetBytes)
            .Select(b => b.Hash().SHA256())
            .ToList();

        // act, assert
        for (int i = 1; i < hashes.Count; i+=10_000)
            uniqueXorResults.Add(hashes[..i].BitwiseXor().Base64())
                .Should()
                .BeTrue("generated xor result is supposed to be unique");
    }
}


[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record HashedObjectBase
{
    [DataMember, MemoryPackOrder(0)] public string Id { get; init; } = "";
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record HashedObject : HashedObjectBase
{
    [DataMember, MemoryPackOrder(1)] public string Name { get; init; } = "";
}
