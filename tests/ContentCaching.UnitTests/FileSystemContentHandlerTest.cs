using System.Net;
using System.Security.Cryptography;
using System.Text;
using ActualLab.Generators;
using ActualLab.IO;

namespace ActualChat.ContentCaching.UnitTests;

public sealed partial class FileSystemContentHandlerTest : IDisposable
{
    private readonly FilePath _directory =
        FilePath.GetApplicationTempDirectory() & $"content-{RandomStringGenerator.Default.Next()}";
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, true);
    }

    [Fact]
    public async Task CompletedResponseShouldSurviveRestartWithoutPlaintextFiles()
    {
        // arrange
        const string body = "private image payload that must not appear on disk";
        var source = new TestSource(_ => Response(body));
        var request = new ContentRequest(new Uri("https://cdn.example/image.png?size=128"));
        var handler = Create(source);

        // act
        using var first = await handler.Handle(request);
        (await first!.Content.ReadAsStringAsync()).Should().Be(body);
        var restarted = Create(new TestSource(_ => throw new InvalidOperationException("Offline")));
        using var cached = await restarted.Handle(request);

        // assert
        (await cached!.Content.ReadAsStringAsync()).Should().Be(body);
        cached.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        cached.Headers.ETag!.Tag.Should().Be("\"v1\"");
        var files = GetCacheFiles();
        files.Should().ContainSingle();
        foreach (var file in files) {
            var bytes = await File.ReadAllBytesAsync(file);
            Encoding.UTF8.GetString(bytes).Should().NotContain(body).And.NotContain("image/png");
        }
    }

    [Theory]
    [InlineData("Range")]
    [InlineData("Authorization")]
    [InlineData("Cookie")]
    [InlineData("Accept")]
    public async Task RequestsWithHeadersShouldBypassTheCache(string header)
    {
        // arrange
        var reads = 0;
        var source = new TestSource(_ => Response($"response-{++reads}"));
        var handler = Create(source);
        var request = Request() with { Headers = new Dictionary<string, string> { [header] = "value" } };

        // act
        using var first = await handler.Handle(request);
        using var second = await handler.Handle(request);

        // assert
        (await first!.Content.ReadAsStringAsync()).Should().Be("response-1");
        (await second!.Content.ReadAsStringAsync()).Should().Be("response-2");
        Directory.Exists(_directory).Should().BeFalse();
    }

    [Theory]
    [InlineData("head")]
    [InlineData("post")]
    [InlineData("large")]
    [InlineData("unknown-length")]
    [InlineData("not-found")]
    [InlineData("partial")]
    [InlineData("private")]
    [InlineData("no-store")]
    [InlineData("vary")]
    [InlineData("cookie")]
    public async Task IneligibleResponsesShouldRemainUnread(string scenario)
    {
        // arrange
        using var stream = new ContentHandlerTest.CountingStream("streaming body");
        var content = new StreamContent(stream);
        content.Headers.ContentLength = 14;
        var upstream = new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        var request = Request();
        switch (scenario) {
        case "head":
            request = request with { Method = HttpMethod.Head };
            break;
        case "post":
            request = request with { Method = HttpMethod.Post };
            break;
        case "large":
            content.Headers.ContentLength = 2 * 1024 * 1024;
            break;
        case "unknown-length":
            content.Headers.ContentLength = null;
            stream.IsLengthHidden = true;
            break;
        case "not-found":
            upstream.StatusCode = HttpStatusCode.NotFound;
            break;
        case "partial":
            upstream.StatusCode = HttpStatusCode.PartialContent;
            break;
        case "private":
            upstream.Headers.CacheControl = new() { Private = true };
            break;
        case "no-store":
            upstream.Headers.CacheControl = new() { NoStore = true };
            break;
        case "vary":
            upstream.Headers.Vary.Add("Accept");
            break;
        case "cookie":
            upstream.Headers.TryAddWithoutValidation("Set-Cookie", "secret=value");
            break;
        }
        var handler = Create(new TestSource(_ => upstream));

        // act
        using var response = await handler.Handle(request);

        // assert
        response.Should().BeSameAs(upstream);
        stream.ReadCount.Should().Be(0);
        Directory.Exists(_directory).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CorruptOrWrongKeyEntriesShouldBeRefetched(bool hasWrongKey)
    {
        // arrange
        var source = new TestSource(_ => Response("cached"));
        using (var first = await Create(source).Handle(Request()))
            (await first!.Content.ReadAsStringAsync()).Should().Be("cached");
        if (hasWrongKey)
            RandomNumberGenerator.Fill(_key);
        else {
            var path = GetCacheFiles().Single();
            var bytes = await File.ReadAllBytesAsync(path);
            bytes[^1] ^= 1;
            await File.WriteAllBytesAsync(path, bytes);
        }

        // act
        using var result = await Create(new TestSource(_ => Response("refetched"))).Handle(Request());

        // assert
        (await result!.Content.ReadAsStringAsync()).Should().Be("refetched");
        GetCacheFiles().Should().ContainSingle();
    }

    [Fact]
    public async Task SwappedFilesShouldNotReturnAnotherAssetsContent()
    {
        // arrange
        var firstRequest = Request();
        var secondRequest = Request() with { Url = new Uri("https://cdn.example/other") };
        var handler = Create(new TestSource(r => Response(r.Url.AbsolutePath)));
        using (var first = await handler.Handle(firstRequest)) { }
        var firstPath = GetCacheFiles().Single();
        using (var second = await handler.Handle(secondRequest)) { }
        var secondPath = GetCacheFiles().Single(x => x != firstPath);
        File.Copy(firstPath, secondPath, true);

        // act
        using var result = await handler.Handle(secondRequest);

        // assert
        (await result!.Content.ReadAsStringAsync()).Should().Be("/other");
    }

    [Theory]
    [InlineData("https://cdn.example/asset?v=2")]
    [InlineData("https://other.example/asset")]
    [InlineData("https://cdn.example/asset?account=other")]
    [InlineData("https://cdn.example/asset?signature=other")]
    public async Task UrlsShouldHaveSeparateEntries(string url)
    {
        // arrange
        var reads = 0;
        var handler = Create(new TestSource(_ => Response($"version-{++reads}")));
        using (var first = await handler.Handle(Request())) { }

        // act
        using var second = await handler.Handle(new ContentRequest(new Uri(url)));
        using var cached = await handler.Handle(Request());

        // assert
        (await second!.Content.ReadAsStringAsync()).Should().Be("version-2");
        (await cached!.Content.ReadAsStringAsync()).Should().Be("version-1");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public async Task InvalidLengthShouldNotPublishAnEntry(long declaredLength)
    {
        // arrange
        var source = new TestSource(_ => {
            var response = Response("body");
            response.Content.Headers.ContentLength = declaredLength;
            return response;
        });
        var handler = Create(source);

        // act
        var action = async () => { using var response = await handler.Handle(Request()); };

        // assert
        var error = await action.Should().ThrowAsync<Exception>();
        error.Which.Should().BeOfType(
            declaredLength == 1 ? typeof(InvalidDataException) : typeof(EndOfStreamException));
        Directory.Exists(_directory).Should().BeFalse();
    }

    [Fact]
    public async Task UnwritableCacheShouldStillReturnDownloadedBytes()
    {
        // arrange
        Directory.CreateDirectory(_directory.DirectoryPath);
        await File.WriteAllTextAsync(_directory, "a file blocks the directory");
        var handler = Create(new TestSource(_ => Response("body")));

        try {
            // act
            using var response = await handler.Handle(Request());

            // assert
            (await response!.Content.ReadAsStringAsync()).Should().Be("body");
        }
        finally {
            File.Delete(_directory);
        }
    }

    [Fact]
    public async Task CancellationShouldNotCallDownstream()
    {
        // arrange
        var handler = Create(new TestSource(_ => throw new InvalidOperationException("Must not fetch")));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        // act
        var action = async () => { using var response = await handler.Handle(Request(), cancellation.Token); };

        // assert
        await action.Should().ThrowAsync<OperationCanceledException>();
        Directory.Exists(_directory).Should().BeFalse();
    }

    [Fact]
    public async Task ConcurrentWritesShouldPublishOnlyCompleteEncryptedEntries()
    {
        // arrange
        var handler = Create(new TestSource(_ => Response("same immutable bytes")));

        // act
        await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ => {
            using var response = await handler.Handle(Request());
            (await response!.Content.ReadAsStringAsync()).Should().Be("same immutable bytes");
        }));
        using var cached = await Create(new TestSource(_ => throw new InvalidOperationException("Offline")))
            .Handle(Request());

        // assert
        (await cached!.Content.ReadAsStringAsync()).Should().Be("same immutable bytes");
        GetCacheFiles().Should().ContainSingle();
    }

    [Fact]
    public async Task UnhandledRequestsShouldStayUnhandled()
    {
        // arrange
        var handler = Create(new TestSource(_ => null));

        // act
        using var response = await handler.Handle(Request());

        // assert
        response.Should().BeNull();
        Directory.Exists(_directory).Should().BeFalse();
    }

    [Fact]
    public async Task UnknownFormatShouldBeRefetched()
    {
        // arrange
        using (var first = await Create(new TestSource(_ => Response("cached"))).Handle(Request())) { }
        var path = GetCacheFiles().Single();
        var bytes = await File.ReadAllBytesAsync(path);
        bytes[0] = 2;
        await File.WriteAllBytesAsync(path, bytes);

        // act
        using var response = await Create(new TestSource(_ => Response("refetched"))).Handle(Request());

        // assert
        (await response!.Content.ReadAsStringAsync()).Should().Be("refetched");
    }

    [Fact]
    public async Task CancellationDuringReadShouldDisposeTheSourceWithoutPublishing()
    {
        // arrange
        using var cancellation = new CancellationTokenSource();
        using var stream = new CancellingStream(cancellation);
        var upstream = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) };
        upstream.Content.Headers.ContentLength = 2;
        var handler = Create(new TestSource(_ => upstream));

        // act
        var action = async () => { using var response = await handler.Handle(Request(), cancellation.Token); };

        // assert
        await action.Should().ThrowAsync<OperationCanceledException>();
        stream.CanRead.Should().BeFalse();
        Directory.Exists(_directory).Should().BeFalse();
    }

    [Fact]
    public async Task BufferingShouldDisposeTheSourceButKeepTheResponseReadable()
    {
        // arrange
        using var stream = new ContentHandlerTest.CountingStream("body");
        var upstream = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) };
        var handler = Create(new TestSource(_ => upstream));

        // act
        using var response = await handler.Handle(Request());

        // assert
        stream.CanRead.Should().BeFalse();
        (await response!.Content.ReadAsStringAsync()).Should().Be("body");
    }

    [Fact]
    public async Task NormalizedSignedUrlsShouldShareEntriesAndPreserveDownloadUrls()
    {
        // arrange
        var firstUrl = new Uri("https://cdn.example/image.png?size=128&signature=first&expires=100");
        var renewedUrl = new Uri("https://cdn.example/image.png?size=128&signature=renewed&expires=200");
        var resizedUrl = new Uri("https://cdn.example/image.png?size=256&signature=third&expires=300");
        var downloads = new List<Uri>();
        var source = new TestSource(request => {
            downloads.Add(request.Url);
            return Response($"response-{downloads.Count}");
        });
        var handler = new FileSystemContentHandler(new FileSystemContentHandler.Options {
            Directory = _directory,
            EncryptionKey = _key,
            CacheUrlNormalizer = url => new UriBuilder(url) {
                Query = string.Join('&', url.Query.TrimStart('?').Split('&')
                    .Where(x => !x.StartsWith("signature=") && !x.StartsWith("expires="))),
            }.Uri,
        }, source);

        // act
        using var first = await handler.Handle(new ContentRequest(firstUrl));
        using var renewed = await handler.Handle(new ContentRequest(renewedUrl));
        using var resized = await handler.Handle(new ContentRequest(resizedUrl));

        // assert
        (await first!.Content.ReadAsStringAsync()).Should().Be("response-1");
        (await renewed!.Content.ReadAsStringAsync()).Should().Be("response-1");
        (await resized!.Content.ReadAsStringAsync()).Should().Be("response-2");
        downloads.Should().Equal(firstUrl, resizedUrl);
        GetCacheFiles().Should().HaveCount(2);
    }

    // Private methods

    private string[] GetCacheFiles()
        => Directory.GetFiles(_directory, "*", SearchOption.AllDirectories);

    private static ContentRequest Request()
        => new(new Uri("https://cdn.example/asset"));

    private FileSystemContentHandler Create(IContentHandler source)
        => new(new FileSystemContentHandler.Options { Directory = _directory, EncryptionKey = _key }, source);

    private static HttpResponseMessage Response(string body)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body)),
        };
        response.Content.Headers.ContentType = new("image/png");
        response.Headers.ETag = new("\"v1\"");
        return response;
    }

    // Nested types

    private sealed class CancellingStream(CancellationTokenSource cancellation) : MemoryStream(new byte[2])
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Position > 0)
                cancellation.Cancel();
            return base.ReadAsync(buffer[..1], cancellationToken);
        }
    }

    private sealed class TestSource(Func<ContentRequest, HttpResponseMessage?> handle) : IContentHandler
    {
        public ValueTask<HttpResponseMessage?> Handle(
            ContentRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(handle(request));
    }
}
