using System.Net;
using System.Text;
using ActualChat.Module;

namespace ActualChat.Transcription.UnitTests;

public sealed class SonioxVoicesClientTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public async Task CreateShouldPostMultipartWithFileAndName()
    {
        // arrange
        HttpRequestMessage? sentRequest = null;
        string? sentBody = null;
        var client = NewClient(async request => {
            sentRequest = request;
            sentBody = await request.Content!.ReadAsStringAsync();
            return Respond("""{"id": "v1", "name": "voxt-u1-abcd1234", "models": []}""");
        });
        using var wav = new MemoryStream([1, 2, 3, 4]);

        // act
        var voice = await client.Create("voxt-u1-abcd1234", wav, CancellationToken.None);

        // assert
        sentRequest!.Method.Should().Be(HttpMethod.Post);
        sentRequest.RequestUri!.ToString().Should().Be("https://api.soniox.com/v1/voices");
        sentBody.Should().Contain("name=file").And.Contain("filename=voice.wav");
        sentBody.Should().Contain("name=name").And.Contain("voxt-u1-abcd1234");
        voice.Id.Should().Be("v1");
    }

    [Fact]
    public async Task GetShouldReturnReadyWhenEveryModelIsReady()
    {
        // arrange
        var client = NewClient(_ => Task.FromResult(Respond("""
            {"id": "v1", "name": "n", "models": [
                {"model": "tts-rt-v2", "status": "ready", "error_type": null, "error_message": null}
            ]}
            """)));

        // act
        var voice = await client.Get("v1", CancellationToken.None);

        // assert
        voice.Should().NotBeNull();
        voice!.IsReady.Should().BeTrue();
        voice.IsFailed.Should().BeFalse();
    }

    [Fact]
    public async Task GetShouldReturnNotReadyWhileProcessing()
    {
        // arrange
        var client = NewClient(_ => Task.FromResult(Respond("""
            {"id": "v1", "name": "n", "models": [
                {"model": "tts-rt-v2", "status": "processing", "error_type": null, "error_message": null}
            ]}
            """)));

        // act
        var voice = await client.Get("v1", CancellationToken.None);

        // assert
        voice!.IsReady.Should().BeFalse();
        voice.IsFailed.Should().BeFalse();
    }

    [Fact]
    public async Task GetShouldReturnFailedWhenAnyModelFailed()
    {
        // arrange
        var client = NewClient(_ => Task.FromResult(Respond("""
            {"id": "v1", "name": "n", "models": [
                {"model": "tts-rt-v2", "status": "failed", "error_type": "bad_audio", "error_message": "too short"}
            ]}
            """)));

        // act
        var voice = await client.Get("v1", CancellationToken.None);

        // assert
        voice!.IsReady.Should().BeFalse();
        voice.IsFailed.Should().BeTrue();
    }

    [Fact]
    public async Task GetShouldReturnNullOn404()
    {
        // arrange
        var client = NewClient(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));

        // act
        var voice = await client.Get("missing", CancellationToken.None);

        // assert
        voice.Should().BeNull();
    }

    [Fact]
    public async Task ListShouldPageUntilTheCursorIsNull()
    {
        // arrange
        var requestedCursors = new List<string?>();
        var client = NewClient(request => {
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query);
            requestedCursors.Add(query["cursor"]);
            var isFirstPage = query["cursor"].IsNullOrEmpty();
            var json = isFirstPage
                ? """{"voices": [{"id": "v1", "name": "n1", "models": []}], "next_page_cursor": "v1"}"""
                : """{"voices": [{"id": "v2", "name": "n2", "models": []}], "next_page_cursor": null}""";
            return Task.FromResult(Respond(json));
        });

        // act
        var voices = await client.List(CancellationToken.None);

        // assert
        requestedCursors.Should().Equal(null, "v1");
        voices.Select(x => x.Id).Should().Equal("v1", "v2");
    }

    [Fact]
    public async Task DeleteShouldIgnore404()
    {
        // arrange
        var deletedUrls = new List<string>();
        var client = NewClient(request => {
            deletedUrls.Add(request.RequestUri!.ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });

        // act
        var act = () => client.Delete("missing", CancellationToken.None);

        // assert
        await act.Should().NotThrowAsync();
        deletedUrls.Should().Equal("https://api.soniox.com/v1/voices/missing");
    }

    // Private methods

    private static HttpResponseMessage Respond(string json)
        => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private SonioxVoicesClient NewClient(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond)
    {
        var services = new ServiceCollection()
            .AddSingleton(new CoreServerSettings { SonioxKey = "test" })
            .AddTestLogging(Out);
        services.AddHttpClient(SonioxClient.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new RespondingHandler(respond));
        return new SonioxVoicesClient(services.BuildServiceProvider());
    }

    // Nested types

    private sealed class RespondingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => respond.Invoke(request);
    }
}
