using System.IO.Pipelines;
using System.Net;
using ActualChat.Module;

namespace ActualChat.Transcription.UnitTests;

public sealed class SonioxTtsClientTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void SplitTextShouldSplitLongTextIntoBoundedParts()
    {
        // arrange
        var text = string.Concat(Enumerable.Repeat("Sentence one. ", 800));

        // act
        var parts = SonioxTtsClient.SplitText(text, 5000).ToList();

        // assert
        parts.Count.Should().BeGreaterThanOrEqualTo(3);
        parts.Should().OnlyContain(p => p.Length <= 5000);
        parts.Should().OnlyContain(p => p.EndsWith('.') || p.EndsWith(' '));
        string.Concat(parts).Should().Be(text);
    }

    [Fact(Timeout = 15_000)]
    public async Task GenerateShouldWritePcmAsTheResponseArrives()
    {
        // arrange
        var body = new Pipe();
        var client = NewClient(_ => new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new StreamContent(body.Reader.AsStream()),
        });
        var pcm = Channel.CreateUnbounded<byte[]>();
        var ct = CancellationToken.None;

        // act
        var generateTask = client.Generate("en", "Adrian", "Hello", pcm.Writer, ct);
        await body.Writer.WriteAsync(new byte[1920], ct);
        var first = await pcm.Reader.ReadAsync(ct).AsTask().WaitAsync(TimeSpan.FromSeconds(5), ct);
        var rest = new byte[100_000];
        await body.Writer.WriteAsync(rest, ct);
        await body.Writer.CompleteAsync();
        await generateTask;
        var chunks = await pcm.Reader.ReadAllAsync(ct).ToListAsync(ct);

        // assert
        first.Should().NotBeEmpty("the first PCM bytes are written before the body is complete");
        chunks.Count.Should().BeGreaterThan(1, "a body larger than the read buffer arrives in several chunks");
        (first.Length + chunks.Sum(x => x.Length)).Should().Be(1920 + rest.Length);
    }

    // Private methods

    private SonioxTtsClient NewClient(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var services = new ServiceCollection()
            .AddSingleton(new CoreServerSettings { SonioxKey = "test" })
            .AddTestLogging(Out);
        services.AddHttpClient(SonioxTtsClient.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new RespondingHandler(respond));
        return new SonioxTtsClient(services.BuildServiceProvider());
    }

    // Nested types

    private sealed class RespondingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(respond.Invoke(request));
    }
}
