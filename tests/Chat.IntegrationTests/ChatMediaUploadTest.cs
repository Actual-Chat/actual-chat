using System.Net;
using System.Net.Http.Headers;
using ActualChat.App.Server;
using ActualChat.Chat.Controllers;
using ActualChat.Controllers;
using ActualChat.Resilience;
using ActualChat.Security;
using ActualChat.Testing.Host;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public sealed class ChatMediaUploadTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact]
    public void UploadKeepsFormValueModelBindingEnabled()
    {
        // act
        var attributes = typeof(ChatMediaController)
            .GetMethod(nameof(ChatMediaController.Upload))!
            .GetCustomAttributes(true);

        // assert
        attributes.Should().NotContain(attribute => attribute is DisableFormValueModelBindingAttribute);
    }

    [Fact]
    public async Task NonMemberCannotUploadToChat()
    {
        // arrange
        await using var member = AppHost.NewBlazorTester(Out);
        await member.SignInAsUniqueBob();
        var (chatId, _) = await member.CreateChat(false);
        await using var nonMember = AppHost.NewBlazorTester(Out);
        await nonMember.SignInAsUniqueAlice();

        // act
        using var memberResponse = await Upload(member.Session, chatId);
        using var nonMemberResponse = await Upload(nonMember.Session, chatId);

        // assert
        memberResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        nonMemberResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GuestCannotUploadToChatThatDisallowsGuests()
    {
        // arrange
        await using var owner = AppHost.NewBlazorTester(Out);
        await owner.SignInAsUniqueBob();
        var (chatId, _) = await owner.CreateChat(c => c with {
            IsPublic = true,
            AllowGuestAuthors = false,
        });
        await using var guest = AppHost.NewBlazorTester(Out);

        // act
        using var response = await Upload(guest.Session, chatId);

        // assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GuestCanUploadToChatThatAllowsGuests()
    {
        // arrange
        await using var owner = AppHost.NewBlazorTester(Out);
        await owner.SignInAsUniqueBob();
        var (chatId, _) = await owner.CreateChat(c => c with {
            IsPublic = true,
            AllowGuestAuthors = true,
        });
        await using var guest = AppHost.NewBlazorTester(Out);

        // act
        using var response = await Upload(guest.Session, chatId);

        // assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RefusesAvatarUploadPastBudget()
    {
        // arrange
        var policy = new CountingRateLimitPolicy(RateLimitClass.UploadCreation, 2);
        await using var appHost = await NewAppHost("avatar-upload-budget", options => options with {
            ConfigureServices = (_, services)
                => services.Replace(ServiceDescriptor.Singleton<RateLimitPolicy>(policy)),
        });
        await using var tester = appHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();

        // act
        using var first = await UploadAvatar(appHost, tester.Session);
        using var second = await UploadAvatar(appHost, tester.Session);
        using var pastBudget = await UploadAvatar(appHost, tester.Session);

        // assert
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        pastBudget.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task RefusesChatUploadPastBudget()
    {
        // arrange
        var policy = new CountingRateLimitPolicy(RateLimitClass.UploadCreation, 2);
        await using var appHost = await NewAppHost("chat-upload-budget", options => options with {
            ConfigureServices = (_, services)
                => services.Replace(ServiceDescriptor.Singleton<RateLimitPolicy>(policy)),
        });
        await using var tester = appHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var (chatId, _) = await tester.CreateChat(false);

        // act
        using var first = await Upload(appHost, tester.Session, chatId);
        using var second = await Upload(appHost, tester.Session, chatId);
        using var pastBudget = await Upload(appHost, tester.Session, chatId);

        // assert
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        pastBudget.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    // Private methods

    private Task<HttpResponseMessage> Upload(Session session, ChatId chatId)
        => Upload(AppHost, session, chatId);

    private static Task<HttpResponseMessage> Upload(AppHost appHost, Session session, ChatId chatId)
        => PostImage(appHost, session, $"api/chat-media/{Uri.EscapeDataString(chatId.Value)}/upload");

    private static Task<HttpResponseMessage> UploadAvatar(AppHost appHost, Session session)
        => PostImage(appHost, session, "api/avatars/upload-picture");

    private static async Task<HttpResponseMessage> PostImage(AppHost appHost, Session session, string requestUri)
    {
        var secureTokens = appHost.Services.GetRequiredService<ISecureTokens>();
        var sessionToken = await secureTokens.CreateForSession(session);
        using var client = appHost.NewHttpClient();
        client.DefaultRequestHeaders.Add(Constants.Session.HeaderName, sessionToken.Token);
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(TestImages.CreatePng(10, 10));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", "image.png");
        return await client.PostAsync(requestUri, content);
    }

    // Nested types

    private sealed class CountingRateLimitPolicy(RateLimitClass rateLimitClass, int limit) : RateLimitPolicy
    {
        private int _count;

        public override bool IsCharged(RateLimitClass actualClass, RateLimitIdentityKind kind)
            => actualClass == rateLimitClass
                && kind is RateLimitIdentityKind.UserId or RateLimitIdentityKind.IP;

        public override ValueTask Check(
            string method,
            RateLimitClass actualClass,
            ReadOnlySpan<RateLimitIdentity> identities,
            CancellationToken cancellationToken = default)
        {
            if (actualClass != rateLimitClass)
                return default;

            if (Interlocked.Increment(ref _count) <= limit)
                return default;

            throw StandardError.RateLimitExceeded(TimeSpan.FromMinutes(1));
        }
    }
}
