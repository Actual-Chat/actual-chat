using System.Net;
using ActualChat.Testing.Host;
using ActualChat.Users.Flows;

namespace ActualChat.Users.IntegrationTests.Flows;

public class UserSignInFlowTest(ITestOutputHelper @out)
    : AppHostTestBase($"x-{nameof(UserSignInFlowTest)}", TestAppHostOptions.Default, @out)
{
    [Fact]
    public async Task ShouldImportGooglePictureForDefaultAvatar()
    {
        using var cts = NewTestCts();
        var ct = cts.Token;
        var pictureUrl = "https://lh3.googleusercontent.com/test-avatar";
        var pictureBytes = TestImages.CreatePng(16, 16);
        var handler = new HttpHandlerMock()
            .Setup(pictureUrl, _ => {
                var response = new HttpResponseMessage(HttpStatusCode.OK) {
                    Content = new ByteArrayContent(pictureBytes),
                };
                response.Content.Headers.ContentType = new("image/png");
                return response;
            });

        await using var h = await NewAppHost(options => options with {
            ConfigureServices = (_, services) => {
                services.AddSingleton<IHttpClientFactory>(_ => new HttpClientFactoryMock(handler));
            },
        });

        var session = Session.New();
        var account = TestAuthExt
            .NewAccount("GooglePicture")
            .WithClaim(Constants.User.Claims.GooglePicture, pictureUrl);
        account = await h.SignIn(session, account, ct);

        var flowHub = h.Services.FlowHub();
        var accountsBackend = h.Services.GetRequiredService<IAccountsBackend>();
        var mediaBackend = h.Services.GetRequiredService<IMediaBackend>();

        await ComputedTest.When(async innerCt => {
            var flow = await flowHub.TryGet<UserSignInFlow>(account.Id.Value, innerCt);
            flow.Should().NotBeNull();
            flow!.IsAvatarUpdated.Should().BeTrue();

            var updatedAccount = await accountsBackend.Get(account.Id, innerCt).Require();
            updatedAccount.Avatar.MediaId.Should().NotBeNull();
            updatedAccount.Avatar.AvatarKey.Should().BeEmpty();
            updatedAccount.Avatar.PictureUrl.Should().BeEmpty();

            var media = await mediaBackend.GetFull(updatedAccount.Avatar.MediaId, innerCt);
            media.Should().NotBeNull();
            media!.Kind.Should().Be(MediaKind.UserAvatarPicture);
            media.BlobId.Should().NotBeEmpty();
            media.ContentType.Should().Be("image/png");
        }, TimeSpan.FromSeconds(30));
    }

    private sealed class HttpClientFactoryMock(HttpHandlerMock handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => new(handler, disposeHandler: false);
    }

    private sealed class HttpHandlerMock : HttpMessageHandler
    {
        private readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _responseFactories =
            new(StringComparer.OrdinalIgnoreCase);

        public HttpHandlerMock Setup(string url, Func<HttpRequestMessage, HttpResponseMessage> factory)
        {
            _responseFactories[url] = factory;
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(_responseFactories[request.RequestUri?.AbsoluteUri ?? ""].Invoke(request));
    }
}
