using System.Net;
using ActualChat.Resilience;
using ActualChat.Rpc;
using ActualChat.Rpc.Internal;
using ActualChat.Security;
using ActualChat.Testing.Host;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.JSInterop;

namespace ActualChat.Users.IntegrationTests;

[Collection(nameof(UserCollection))]
public class SetupSessionTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task ConcurrentUpsertsShouldLeaveOneGuestAccount()
    {
        // arrange
        var appHost = AppHost;
        var services = appHost.Services;
        var commander = services.Commander();
        await using var tester = appHost.NewWebClientTester(Out);
        var session = tester.Session;

        // act
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => commander.Call(new SessionsBackend_Upsert(session)))
            .ToArray();
        await Task.WhenAll(tasks);

        // assert
        var accounts = services.GetRequiredService<IAccounts>();
        var sessionInfo = await accounts.GetSessionInfo(session, CancellationToken.None);
        sessionInfo.Should().NotBeNull();
        var guestId = sessionInfo.GetGuestId();
        guestId.Should().NotBeNull();
        guestId.IsGuest.Should().BeTrue();
    }

    [Fact]
    public async Task ParallelUpsertsShouldGiveEverySessionItsOwnGuestAccount()
    {
        // arrange
        var appHost = AppHost;
        var services = appHost.Services;
        var commander = services.Commander();
        await using var tester = appHost.NewWebClientTester(Out);
        var accounts = services.GetRequiredService<IAccounts>();

        // act & assert
        await Parallel.ForEachAsync(Enumerable.Range(0, 10), async (_, cancellationToken) => {
            var session = Session.New();
            await commander.Call(new SessionsBackend_Upsert(session), cancellationToken);
            var sessionInfo = await accounts.GetSessionInfo(session, cancellationToken);
            sessionInfo.Should().NotBeNull();
            var guestId = sessionInfo.GetGuestId();
            guestId.Should().NotBeNull();
            guestId.IsGuest.Should().BeTrue();
        });
    }

    [Fact]
    public async Task ServerConnectionShouldCarryTheForwardedAddress()
    {
        // arrange
        var helpers = AppHost.Services.GetRequiredService<RpcBackendHelpers>();
        var session = Session.New();
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.99");
        httpContext.Request.Headers[Constants.Session.HeaderName] = session.Id;
        httpContext.Request.Headers["X-Forwarded-For"] = "203.0.113.9";
        var properties = PropertyBag.Empty.KeylessSet<HttpContext>(httpContext);

        // act
        var connection = await helpers.GetServerConnection(null!, null!, properties, CancellationToken.None);

        // assert
        var backendConnection = connection.Should().BeOfType<RpcBackendConnection>().Subject;
        backendConnection.Session.Should().Be(session);
        backendConnection.RemoteIPAddress.Should().Be("203.0.113.9");
        connection.GetRemoteIPAddress().Should().Be("203.0.113.9");
    }

    [Fact]
    public async Task SessionCreationShouldBeRefusedPastBudget()
    {
        // arrange
        var policy = new CountingRateLimitPolicy(RateLimitClass.SessionCreation, 2);
        await using var appHost = await NewAppHost("mobile-session-budget", options => options with {
            ConfigureServices = (_, services) => {
                services.Replace(ServiceDescriptor.Singleton<RateLimitPolicy>(policy));
            },
        });
        await using var tester = appHost.NewWebClientTester(
            Out,
            services => {
                services.AddSingleton(Mock.Of<IJSRuntime>(MockBehavior.Strict));
                services.RemoveAll<ISessionResolver>();
                services.AddSingleton<TrueSessionResolver>();
                services.AddSingleton<ISessionResolver>(
                    c => c.GetRequiredService<TrueSessionResolver>());
            });
        await tester.SignInAsNew("MobileSession");
        var clientServices = tester.ClientServices;
        clientServices.GetRequiredService<TrueSessionResolver>().Session = tester.Session;
        var mobileSessions = clientServices.GetRequiredService<IMobileSessions>();

        // act
        var first = await mobileSessions.CreateSession("TestApp/1.0", default);
        var second = await mobileSessions.CreateSession("TestApp/1.0", default);
        var act = () => mobileSessions.CreateSession("TestApp/1.0", default);

        // assert
        first.Should().NotBe(second);
        await act.Should().ThrowAsync<Exception>().WithMessage("*Too many requests*");
        policy.Count.Should().Be(3);
    }

    [Fact]
    public async Task AppSessionShouldKeepItsAppKindAcrossDescriptionUpdates()
    {
        // arrange
        var commander = AppHost.Services.Commander();
        var accounts = AppHost.Services.GetRequiredService<IAccounts>();
        await using var tester = AppHost.NewWebClientTester(Out);
        var session = tester.Session;
        await commander.Call(new SessionsBackend_Upsert(session) {
            Description = $"{AppKind.Ios.ToUserAgent("1.0")} Mozilla/5.0 (iPhone) AppleWebKit/605.1.15",
        });

        // act
        // The native OAuth close flow describes the session with the system browser's agent.
        await commander.Call(new SessionsBackend_Upsert(session) {
            Description = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36",
        });
        var afterBrowser = await accounts.GetSessionInfo(session, CancellationToken.None);
        await commander.Call(new SessionsBackend_Upsert(session) {
            Description = $"{AppKind.Ios.ToUserAgent("2.0")} Mozilla/5.0 (iPhone) AppleWebKit/605.1.15",
        });
        var afterAppUpdate = await accounts.GetSessionInfo(session, CancellationToken.None);

        // assert
        afterBrowser!.Description.Should().StartWith(AppKind.Ios.ToUserAgent("1.0"));
        afterAppUpdate!.Description.Should().StartWith(AppKind.Ios.ToUserAgent("2.0"));
    }

    private sealed class CountingRateLimitPolicy(RateLimitClass rateLimitClass, int limit) : RateLimitPolicy
    {
        private int _count;
        public int Count => _count;

        public override bool IsCharged(RateLimitClass actualClass, RateLimitIdentityKind kind)
            => RateLimitBudgets.Default.Get(actualClass, kind) is not null;

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
