using System.Net;
using ActualChat.AspNetCore;
using ActualChat.Resilience;
using ActualChat.Resilience.Internal;
using ActualChat.Rpc;
using ActualChat.Rpc.Internal;
using ActualLab.Rpc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace ActualChat.Core.Server.UnitTests.Resilience;

public class RateLimitBudgetsTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void AuthCommandsGetTheirOwnCostClass()
    {
        // arrange
        var classResolver = RateLimitClassResolver.Default;

        // act
        var sendTotp = classResolver.Resolve("EmailAuth_SendTotp", true);
        var validateTotp = classResolver.Resolve("PhoneAuth_ValidateTotp", true);
        var useInvite = classResolver.Resolve("Invites_Use", true);
        var confirmRegister = classResolver.Resolve("Accounts_ConfirmRegister", true);
        var postMessage = classResolver.Resolve("Chats_UpsertTextEntry", true);

        // assert
        sendTotp.Should().Be(RateLimitClass.Auth);
        validateTotp.Should().Be(RateLimitClass.Auth);
        useInvite.Should().Be(RateLimitClass.Auth);
        confirmRegister.Should().Be(RateLimitClass.Auth);
        postMessage.Should().Be(RateLimitClass.Command);
    }

    [Fact]
    public void AuthBudgetsAreTrackedSeparatelyAndAreTighter()
    {
        // arrange
        var budgets = RateLimitBudgets.Default;

        // act
        var auth = budgets.Get(RateLimitClass.Auth, RateLimitIdentityKind.Session);
        var write = budgets.Get(RateLimitClass.Command, RateLimitIdentityKind.Session);
        var httpRead = budgets.Get(RateLimitClass.HttpRead, RateLimitIdentityKind.Session);
        var authTarget = budgets.Get(RateLimitClass.Auth, RateLimitIdentityKind.Target);
        var authAddress = budgets.Get(RateLimitClass.Auth, RateLimitIdentityKind.IP);

        // assert
        auth.Should().NotBeNull();
        write.Should().NotBeNull();
        httpRead.Should().NotBeNull();
        authTarget.Should().NotBeNull();
        authAddress.Should().NotBeNull();
        Rate(auth!).Should().BeLessThan(Rate(write!));
        Rate(write!).Should().BeLessThan(Rate(httpRead!));
    }

    [Fact]
    public void RpcReadsAreNotChargedAtAll()
    {
        // arrange
        var classResolver = RateLimitClassResolver.Default;
        var budgets = RateLimitBudgets.Default;

        // act
        var read = classResolver.Resolve("Chats_GetTile", false);
        var command = classResolver.Resolve("Chats_UpsertTextEntry", true);
        var httpReadAddress = budgets.Get(RateLimitClass.HttpRead, RateLimitIdentityKind.IP);
        var writeAddress = budgets.Get(RateLimitClass.Command, RateLimitIdentityKind.IP);
        var httpReadSession = budgets.Get(RateLimitClass.HttpRead, RateLimitIdentityKind.Session);

        // assert
        read.Should().BeNull();
        command.Should().Be(RateLimitClass.Command);
        httpReadAddress.Should().BeNull();
        writeAddress.Should().BeNull();
        httpReadSession.Should().NotBeNull();
    }

    [Fact]
    public void CreationAndProviderBudgetsAreConfigured()
    {
        // arrange
        var budgets = RateLimitBudgets.Default;

        // act
        var sessionAccount = budgets.Get(RateLimitClass.SessionCreation, RateLimitIdentityKind.UserId);
        var sessionAddress = budgets.Get(RateLimitClass.SessionCreation, RateLimitIdentityKind.IP);
        var gifAccount = budgets.Get(RateLimitClass.GifProvider, RateLimitIdentityKind.UserId);
        var gifAddress = budgets.Get(RateLimitClass.GifProvider, RateLimitIdentityKind.IP);
        var uploadCreationAccount = budgets.Get(RateLimitClass.UploadCreation, RateLimitIdentityKind.UserId);
        var uploadCreationAddress = budgets.Get(RateLimitClass.UploadCreation, RateLimitIdentityKind.IP);
        var uploadBytesAccount = budgets.Get(RateLimitClass.UploadBytes, RateLimitIdentityKind.UserId);
        var uploadBytesAddress = budgets.Get(RateLimitClass.UploadBytes, RateLimitIdentityKind.IP);

        // assert
        sessionAccount.Should().Be(new SlidingWindowBudget(100, TimeSpan.FromDays(1)));
        sessionAddress.Should().Be(new SlidingWindowBudget(100_000, TimeSpan.FromDays(1)));
        gifAccount.Should().Be(new SlidingWindowBudget(1_000, TimeSpan.FromHours(1)));
        gifAddress.Should().Be(new SlidingWindowBudget(100_000, TimeSpan.FromHours(1)));
        uploadCreationAccount.Should().Be(new SlidingWindowBudget(10_000, TimeSpan.FromDays(1)));
        uploadCreationAddress.Should().Be(new SlidingWindowBudget(100_000, TimeSpan.FromDays(1)));
        uploadBytesAccount.Should().Be(new SlidingWindowBudget(10_486, TimeSpan.FromDays(1)));
        uploadBytesAddress.Should().Be(new SlidingWindowBudget(104_858, TimeSpan.FromDays(1)));
    }

    [Fact]
    public void ExhaustedBudgetYieldsADedicatedError()
    {
        // act
        var error = StandardError.RateLimitExceeded(TimeSpan.FromSeconds(4.2));
        var restored = new ExceptionInfo(error).ToException();

        // assert
        error.Should().BeOfType<RateLimitExceededException>();
        restored.Should().BeOfType<RateLimitExceededException>()
            .Which.RetryDelay.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void SharedAddressesAreNotChargedAsOneCaller()
    {
        // act
        var loopback = RateLimitIdentity.ForIP(IPAddress.Parse("127.0.0.1"));
        var ipV6Loopback = RateLimitIdentity.ForIP(IPAddress.Parse("::1"));
        var privateA = RateLimitIdentity.ForIP(IPAddress.Parse("10.4.1.7"));
        var privateB = RateLimitIdentity.ForIP(IPAddress.Parse("172.20.0.9"));
        var privateC = RateLimitIdentity.ForIP(IPAddress.Parse("192.168.1.5"));
        var cgnat = RateLimitIdentity.ForIP(IPAddress.Parse("100.100.0.1"));
        var uniqueLocal = RateLimitIdentity.ForIP(IPAddress.Parse("fd00::1"));
        var mappedLoopback = RateLimitIdentity.ForIP(IPAddress.Parse("::ffff:127.0.0.1"));
        var public4 = RateLimitIdentity.ForIP(IPAddress.Parse("203.0.113.9"));
        var public6 = RateLimitIdentity.ForIP(IPAddress.Parse("2001:db8::1"));

        // assert
        loopback.Should().BeNull();
        ipV6Loopback.Should().BeNull();
        privateA.Should().BeNull();
        privateB.Should().BeNull();
        privateC.Should().BeNull();
        cgnat.Should().BeNull();
        uniqueLocal.Should().BeNull();
        mappedLoopback.Should().BeNull();
        public4.Should().Be(new RateLimitIdentity(RateLimitIdentityKind.IP, "203.0.113.9"));
        public6.Should().Be(new RateLimitIdentity(RateLimitIdentityKind.IP, "2001:db8::1"));
    }

    [Fact]
    public void RpcConnectionYieldsTheSameAddressAsItsHttpContext()
    {
        // arrange
        var httpContext = NewHttpContext("GET", "198.51.100.7", "203.0.113.9");
        var properties = PropertyBag.Empty.KeylessSet<HttpContext>(httpContext);

        // act
        var backend = new RpcBackendConnection(null!, properties, Session.New(),
                RateLimitIdentity.ToChargeableIP(httpContext.GetRemoteIPAddress()))
            .GetRemoteIPAddress();
        var plain = new RpcConnection(null!, properties).GetRemoteIPAddress();
        var bare = new RpcConnection(null!, PropertyBag.Empty).GetRemoteIPAddress();
        var none = ((RpcConnection?)null).GetRemoteIPAddress();

        // assert
        backend.Should().Be("203.0.113.9");
        plain.Should().Be("203.0.113.9");
        bare.Should().BeNull();
        none.Should().BeNull();
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    public async Task HttpCallLimiterChargesTheSessionOnly(string httpMethod)
    {
        // arrange
        var policy = new TestRateLimitPolicy();
        var session = Session.New();
        var context = NewHttpContext(httpMethod, "198.51.100.7", "203.0.113.9", session);

        // act
        await NewHttpRateLimitMiddleware(policy, _ => Task.CompletedTask).Invoke(context);

        // assert
        policy.Identities.Should().Equal([new RateLimitIdentity(RateLimitIdentityKind.Session, session.Id)]);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    public async Task HttpCallLimiterRejectsWhenTheSessionBudgetIsExhausted(string httpMethod)
    {
        // arrange
        var policy = new TestRateLimitPolicy { DeniedKind = RateLimitIdentityKind.Session };
        var context = NewHttpContext(httpMethod, "198.51.100.7", "203.0.113.9", Session.New());
        var isNextInvoked = false;

        // act
        await NewHttpRateLimitMiddleware(policy, _ => {
            isNextInvoked = true;
            return Task.CompletedTask;
        }).Invoke(context);

        // assert
        context.Response.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
        isNextInvoked.Should().BeFalse();
    }

    // Private methods

    private static double Rate(SlidingWindowBudget budget)
        => budget.Limit / budget.Window.TotalSeconds;

    private static DefaultHttpContext NewHttpContext(
        string httpMethod,
        string peer,
        string forwardedFor,
        Session? session = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = httpMethod;
        context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
        context.Request.Headers["X-Forwarded-For"] = forwardedFor;
        if (session is not null)
            context.Request.Headers[Constants.Session.HeaderName] = session.Id;
        var action = new ControllerActionDescriptor { ControllerName = "Test", ActionName = "Run" };
        context.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(action),
            "Test.Run"));
        return context;
    }

    private static HttpRateLimitMiddleware NewHttpRateLimitMiddleware(RateLimitPolicy policy, RequestDelegate next)
    {
        var services = new ServiceCollection()
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton(policy)
            .AddSingleton(RateLimitBudgets.Default)
            .AddSingleton<RateLimitUserIdResolver>(_ => (_, _) => default)
            .BuildServiceProvider();
        return new HttpRateLimitMiddleware(next, services);
    }

    // Nested types

    private sealed class TestRateLimitPolicy : RateLimitPolicy
    {
        public List<RateLimitIdentity> Identities { get; } = new();
        public RateLimitIdentityKind? DeniedKind { get; init; }

        public override bool IsCharged(RateLimitClass rateLimitClass, RateLimitIdentityKind kind)
            => RateLimitBudgets.Default.Get(rateLimitClass, kind) is not null;

        public override ValueTask Check(
            string method,
            RateLimitClass rateLimitClass,
            ReadOnlySpan<RateLimitIdentity> identities,
            CancellationToken cancellationToken = default)
        {
            foreach (var identity in identities)
                Identities.Add(identity);
            if (DeniedKind is { } deniedKind && Identities.Any(x => x.Kind == deniedKind))
                throw StandardError.RateLimitExceeded(TimeSpan.FromSeconds(7));

            return default;
        }
    }
}
