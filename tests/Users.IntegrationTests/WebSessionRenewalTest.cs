using ActualChat.Testing.Host;
using ActualChat.Users.Db;
using ActualLab.Fusion.EntityFramework;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace ActualChat.Users.IntegrationTests;

[Trait("Category", "Slow")]
public sealed class WebSessionRenewalTest(ITestOutputHelper @out)
    : AppHostTestBase($"x-{nameof(WebSessionRenewalTest)}", TestAppHostOptions.Default, @out)
{
    private static readonly TimeSpan Period = Constants.Session.LastSeenAtUpdatePeriod;
    private static readonly TimeSpan Lifetime = CoreConstants.Session.SessionExpirationTime;
    private static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

    [Fact(Timeout = 120_000)]
    public async Task PageLoadPastTheThrottleShouldExtendTheSessionAndRenewItsCookie()
    {
        await using var h = await NewAppHost();

        // arrange
        var session = await NewSession(h, expiresIn: TimeSpan.FromDays(1), lastSeenAgo: Period + Tolerance);
        var httpContext = NewHttpContext(h, session);

        // act
        var authState = await h.Services.GetRequiredService<AuthHelper>().UpdateAuthState(httpContext);

        // assert
        authState.Session.Should().Be(session);
        await AssertExpiresIn(h, session, Lifetime);
        var setCookie = SetCookieHeaderValue.Parse(httpContext.Response.Headers.SetCookie.ToString());
        setCookie.Value.ToString().Should().Be(session.Id);
        var cookieLifetime = setCookie.Expires!.Value - DateTimeOffset.UtcNow;
        cookieLifetime.Should().BeCloseTo(Lifetime, Tolerance);
    }

    [Fact(Timeout = 120_000)]
    public async Task PageLoadWithinTheThrottleShouldNotWriteTheSession()
    {
        await using var h = await NewAppHost();

        // arrange
        var recently = TimeSpan.FromMinutes(1);
        var session = await NewSession(h, expiresIn: TimeSpan.FromDays(5), lastSeenAgo: recently);
        var before = await GetSessionInfo(h, session);
        var httpContext = NewHttpContext(h, session);

        // act
        await h.Services.GetRequiredService<AuthHelper>().UpdateAuthState(httpContext);

        // assert
        var after = await GetSessionInfo(h, session);
        after.Version.Should().Be(before.Version);
        after.ExpiresAt.Should().Be(before.ExpiresAt);
    }

    [Fact(Timeout = 120_000)]
    public async Task CheckInWithoutTheFlagShouldNotWrite()
    {
        await using var h = await NewAppHost();

        // arrange
        var session = await NewSession(h, expiresIn: TimeSpan.FromDays(5), lastSeenAgo: TimeSpan.FromHours(2));
        var before = await GetSessionInfo(h, session);

        // act
        await CheckIn(h, session, mustExtendSession: false);

        // assert
        var after = await GetSessionInfo(h, session);
        after.Version.Should().Be(before.Version);
    }

    [Fact(Timeout = 120_000)]
    public async Task CheckInWithTheFlagShouldExtendTheSession()
    {
        await using var h = await NewAppHost();

        // arrange
        var session = await NewSession(h, expiresIn: TimeSpan.FromDays(5), lastSeenAgo: Period + Tolerance);

        // act
        await CheckIn(h, session, mustExtendSession: true);

        // assert
        await AssertExpiresIn(h, session, Lifetime);
    }

    [Fact(Timeout = 120_000)]
    public async Task FlaggedCheckInWithinThePeriodShouldNotWrite()
    {
        await using var h = await NewAppHost();

        // arrange
        var recently = TimeSpan.FromMinutes(1);
        var session = await NewSession(h, expiresIn: TimeSpan.FromDays(5), lastSeenAgo: recently);
        var before = await GetSessionInfo(h, session);

        // act
        await CheckIn(h, session, mustExtendSession: true);
        await CheckIn(h, session, mustExtendSession: true);

        // assert
        var after = await GetSessionInfo(h, session);
        after.Version.Should().Be(before.Version);
    }

    [Theory(Timeout = 120_000)]
    [InlineData(SessionKind.ApiKey)]
    [InlineData(SessionKind.OAuth)]
    public async Task CheckInWithTheFlagShouldNotWriteOtherSessionKinds(SessionKind kind)
    {
        await using var h = await NewAppHost();

        // arrange
        var session = kind == SessionKind.ApiKey ? SessionExt.NewApiKey() : SessionExt.NewOAuth();
        var upsertCmd = new SessionsBackend_Upsert(session);
        await h.Services.Commander().Call(upsertCmd, true);
        await SetTimes(h, session, expiresIn: TimeSpan.FromDays(60), lastSeenAgo: TimeSpan.FromHours(2));
        var before = await GetSessionInfo(h, session);

        // act
        await CheckIn(h, session, mustExtendSession: true);

        // assert
        var after = await GetSessionInfo(h, session);
        after.Version.Should().Be(before.Version);
    }

    [Fact(Timeout = 120_000)]
    public async Task CheckInWithTheFlagShouldNotReviveAnExpiredSession()
    {
        await using var h = await NewAppHost();
        var commander = h.Services.Commander();

        // arrange
        var session = await NewSession(h, expiresIn: -TimeSpan.FromHours(1), lastSeenAgo: Period + Tolerance);
        var before = await GetSessionInfo(h, session);

        // act
        await CheckIn(h, session, mustExtendSession: true);
        var upsertCmd = new SessionsBackend_Upsert(session).WithRollingExpiration(h.Services.Clocks().SystemClock.Now);
        var upsertException = await Record.ExceptionAsync(() => commander.Call(upsertCmd, true));

        // assert
        upsertException.Should().BeOfType<InvalidOperationException>();
        var after = await GetSessionInfo(h, session);
        after.IsActive.Should().BeFalse();
        after.ExpiresAt.Should().Be(before.ExpiresAt);
    }

    [Fact(Timeout = 120_000)]
    public async Task MobileUpdateLastSeenAtShouldStillExtendTheSession()
    {
        await using var h = await NewAppHost();
        var sessionsBackend = h.Services.GetRequiredService<ISessionsBackend>();

        // arrange
        var session = await NewSession(h, expiresIn: TimeSpan.FromDays(5), lastSeenAgo: Period + Tolerance);

        // act
        await sessionsBackend.UpdateLastSeenAt(session, null, null);

        // assert
        await AssertExpiresIn(h, session, Lifetime);
    }

    // Private methods

    private static DefaultHttpContext NewHttpContext(TestAppHost h, Session session)
    {
        var httpContext = new DefaultHttpContext {
            RequestServices = h.Services,
            Request = { Method = "GET", Path = "/" },
        };
        httpContext.Request.Headers.Cookie = $"{Constants.Session.CookieName}={session.Id}";
        return httpContext;
    }

    private static async Task<Session> NewSession(
        TestAppHost h, TimeSpan expiresIn, TimeSpan lastSeenAgo)
    {
        var session = Session.New();
        var upsertCmd = new SessionsBackend_Upsert(session);
        await h.Services.Commander().Call(upsertCmd, true);
        await SetTimes(h, session, expiresIn, lastSeenAgo);
        return session;
    }

    private static async Task SetTimes(TestAppHost h, Session session, TimeSpan expiresIn, TimeSpan lastSeenAgo)
    {
        var now = DateTime.UtcNow;
        var expiresAt = now + expiresIn;
        var lastSeenAt = now - lastSeenAgo;
        var dbHub = h.Services.GetRequiredService<DbHub<UsersDbContext>>();
        var dbContext = await dbHub.CreateDbContext(readWrite: true, CancellationToken.None);
        await using var _1 = dbContext.ConfigureAwait(false);
        await dbContext.Sessions
            .Where(x => x.Id == session.Id)
            .ExecuteUpdateAsync(x => x
                .SetProperty(y => y.ExpiresAt, expiresAt)
                .SetProperty(y => y.LastSeenAt, lastSeenAt));
        var accounts = h.Services.GetRequiredService<IAccounts>();
        var sessionsBackend = h.Services.GetRequiredService<ISessionsBackend>();
        using (Invalidation.Begin()) {
            _ = accounts.GetSessionInfo(session, CancellationToken.None);
            _ = sessionsBackend.Get(session, CancellationToken.None);
        }
    }

    private static async Task CheckIn(TestAppHost h, Session session, bool mustExtendSession)
    {
        var checkInCmd = new UserPresences_CheckIn {
            Session = session,
            IsActive = true,
            MustExtendSession = mustExtendSession,
        };
        await h.Services.Commander().Call(checkInCmd);
    }

    private static async Task AssertExpiresIn(TestAppHost h, Session session, TimeSpan expected)
    {
        var sessionInfo = await GetSessionInfo(h, session);
        var actual = sessionInfo.ExpiresAt - h.Services.Clocks().SystemClock.Now;
        actual.Should().BeCloseTo(expected, Tolerance);
    }

    private static async Task<SessionInfoFull> GetSessionInfo(TestAppHost h, Session session)
    {
        var accounts = h.Services.GetRequiredService<IAccounts>();
        var sessionInfo = await accounts.GetSessionInfo(session, CancellationToken.None);
        return sessionInfo!;
    }
}
