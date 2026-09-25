# Arrival, Onboarding and Activation Metrics Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Record how each new user arrived (`SignUp` usage event), which onboarding steps they completed or skipped (`OnboardingStep` usage events), and count funnel steps (link opened → sign-in → joined, banner shown → shared, contacts) as OTLP counters, plus a team-only report doc with SQL/PromQL.

**Architecture:** The client writes the arrival path into the client-writable session temporal `c.Arrival`. `AccountsBackend.OnSignIn` reads it when it creates an account and appends a `SignUp` event to the existing `UsageEvents` log. The onboarding modal reports each step through a new `IUsage` command. Funnel counters live in a new `FunnelMeters` (Core.Server, `usage.funnel.events`). Server-side events are counted in place. Client-only events come in through `Usage_RecordFunnelEvent`.

**Tech Stack:** .NET 11, ActualLab.Fusion (compute services, commander, operation events), EF Core + PostgreSQL (jsonb), Blazor (WASM/Server/MAUI Hybrid), System.Diagnostics.Metrics → OTLP → GCP Managed Prometheus, `Xamarin.Google.Android.InstallReferrer`.

**Spec:** `docs/superpowers/specs/2026-09-25-arrival-onboarding-activation-design.md`

## Global Constraints

- Read `docs/CODING_STYLE.md` before writing code. Key rules:
  - no `Async` suffix
  - no new `///` on members; `//` only where non-obvious
  - Allman braces for types/methods, K&R for everything else
  - a control-flow statement is followed by a blank line
  - `sealed` by default, except Fusion-proxied services
  - `.ConfigureAwait(false)` in service code
  - `TaskCompletionSourceExt.New<T>()`, never `new TaskCompletionSource`
  - Log via `Log`, never `Console`
- Serialization: every new member of a `[DataContract, MessagePackObject]` type gets `[DataMember]` + the next unused `[Key(N)]`. Never renumber.
- No DB migration: `UsageEvents.kind` is an `integer` and `attributes` is `jsonb`.
- `c.Arrival` wire format: `web` | `store` | `join:<id>` | `user:<userId>` | `campaign:<id>`. The id is 1–64 chars from `[A-Za-z0-9_.@-]`.
- Counter name: `usage.funnel.events`, tags `event`, `app` (`AppKind`), and `arrival` (only on `SignUp`).
- Onboarding step names, in order: `TranscriptionTutorial, PlacesTutorial, SummarizationTutorial, Phone, Email, Avatar, Permissions, Languages, DataCollection, Passkey, Finished`.
- A failure in any measurement code must never fail sign-in, onboarding, invite use, sharing or contact linking.
- Tests: FluentAssertions, AAA comments (`// arrange` / `// act` / `// assert`), `Should`-style names, waits only through `TestWait` (read `docs/testing/waiting.md` before adding a wait).
- `CI.slnf` is broken locally, so build by project (see each task).
- Commit after each task with a conventional-commit message ending in `Refs #4802` and the `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` trailer. Never push.

## Review Focus

1. **A hostile or garbled `c.Arrival`** (`join:` with spaces, 500 chars, `web:x`, `foo`) must fall back to `web`/`store` and must never throw out of sign-in. Pinned in Task 1 (`ArrivalInfoTest`) and Task 3 (`InvalidArrivalShouldFallBackAndNotBreakSignUp`).
2. **An existing user signing in on a session that carries `c.Arrival`** must not get a `SignUp` row. Pinned in Task 3 (`ExistingAccountSignInShouldNotRecordSignUp`).
3. **New kinds must not create `UsageDay` rows.** A day row counts as an active day for the review prompt, so a stray row inflates `ActiveDays`. Pinned in Task 2.
4. **A renamed onboarding step component** would silently stop being counted. Pinned in Task 6 (`OnboardingStepNamesShouldMatchModalSteps`).
5. **An undefined `FunnelEvent` value from a client** (e.g. `(FunnelEvent)99`), or a server-only event, must be rejected. Pinned in Task 1 (`FunnelEventTest`) and Task 4.

---

### Task 1: Model types — arrival, funnel events, onboarding step names, usage-event builders

**Files:**
- Create: `src/dotnet/Api/Users/Usage/ArrivalKind.cs`
- Create: `src/dotnet/Api/Users/Usage/ArrivalInfo.cs`
- Create: `src/dotnet/Api/Users/Usage/FunnelEvent.cs`
- Create: `src/dotnet/Api/Users/Usage/OnboardingSteps.cs`
- Modify: `src/dotnet/Api/Users/Usage/UsageEventKind.cs`
- Modify: `src/dotnet/Api/Users/Usage/UsageEvent.cs` (`UsageEventAttributes`)
- Modify: `src/dotnet/Api/Constants.SessionTemporals.cs`
- Modify: `src/dotnet/Users.Contracts/UsageEventSource.cs`
- Test: `tests/Users.UnitTests/Usage/ArrivalInfoTest.cs` (create)
- Test: `tests/Users.UnitTests/Usage/FunnelEventTest.cs` (create)
- Test: `tests/Users.UnitTests/Usage/UsageEventSourceTest.cs` (extend)

**Interfaces:**
- Produces:
  - `enum ArrivalKind { Web, Store, Join, User, Campaign }`
  - `readonly record struct ArrivalInfo(ArrivalKind Kind, string Id = "")` with:
    - `static ArrivalInfo? New(ArrivalKind, string? id = null)`
    - `static ArrivalInfo Fallback(AppKind)`
    - `static ArrivalInfo? FromQuery(string?)`
    - `static bool TryParse(string?, out ArrivalInfo)`
    - `string Format()`
  - `enum FunnelEvent` and `FunnelEventExt.IsClientReportable(this FunnelEvent)`
  - `static class OnboardingSteps` with `Finished`, `All`, `IsValid(string)` and `GetName(Type)`
  - `UsageEventKind.SignUp = 5`, `UsageEventKind.OnboardingStep = 6`, `UsageEventKindExt.IsDayRollup(this UsageEventKind)`
  - `UsageEventAttributes.ArrivalKind` (`ArrivalKind?`, Key 4)
  - `Constants.SessionTemporals.ArrivalKey = "Arrival"`
  - `UsageEventSource.SignUp(ArrivalInfo, Moment)` and `UsageEventSource.OnboardingStep(string, bool, Moment)`
  - All of these live in namespace `ActualChat.Users`, except the constant.

- [ ] **Step 1: Write the failing tests**

`tests/Users.UnitTests/Usage/ArrivalInfoTest.cs`:

```csharp
using ActualChat.Hosting;

namespace ActualChat.Users.UnitTests.Usage;

public class ArrivalInfoTest
{
    [Theory]
    [InlineData("web", ArrivalKind.Web, "")]
    [InlineData("store", ArrivalKind.Store, "")]
    [InlineData("join:inv-123_A", ArrivalKind.Join, "inv-123_A")]
    [InlineData("user:hjp639qb6bp1", ArrivalKind.User, "hjp639qb6bp1")]
    [InlineData("campaign:spring.2026@x", ArrivalKind.Campaign, "spring.2026@x")]
    public void ValidValueShouldRoundTrip(string value, ArrivalKind kind, string id)
    {
        // act
        var isParsed = ArrivalInfo.TryParse(value, out var arrival);

        // assert
        isParsed.Should().BeTrue();
        arrival.Kind.Should().Be(kind);
        arrival.Id.Should().Be(id);
        arrival.Format().Should().Be(value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("foo")]
    [InlineData("web:x")]
    [InlineData("store:x")]
    [InlineData("join:")]
    [InlineData("join")]
    [InlineData("join:has space")]
    [InlineData("campaign:a/b")]
    [InlineData("JOIN:abc")]
    public void InvalidValueShouldNotParse(string? value)
    {
        // act
        var isParsed = ArrivalInfo.TryParse(value, out _);

        // assert
        isParsed.Should().BeFalse();
    }

    [Fact]
    public void OverLongIdShouldBeRejected()
    {
        // arrange
        var id = new string('a', ArrivalInfo.MaxIdLength + 1);

        // act
        var arrival = ArrivalInfo.New(ArrivalKind.Campaign, id);
        var isParsed = ArrivalInfo.TryParse("campaign:" + id, out _);

        // assert
        arrival.Should().BeNull();
        isParsed.Should().BeFalse();
        ArrivalInfo.New(ArrivalKind.Campaign, id[..^1]).Should().NotBeNull("the max length itself is allowed");
    }

    [Theory]
    [InlineData("/?utm_source=x&utm_campaign=spring", "campaign:spring")]
    [InlineData("/chats?c=promo1", "campaign:promo1")]
    [InlineData("utm_source=google-play&utm_campaign=play1", "campaign:play1")]
    [InlineData("/?utm_campaign=spring&c=other", "campaign:spring")]
    [InlineData("/?utm_campaign=has%20space", null)]
    [InlineData("/join/abc", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void FromQueryShouldReadTheCampaign(string? urlOrQuery, string? expected)
    {
        // act
        var arrival = ArrivalInfo.FromQuery(urlOrQuery);

        // assert
        arrival?.Format().Should().Be(expected);
        if (expected is null)
            arrival.Should().BeNull();
    }

    [Theory]
    [InlineData(AppKind.Unknown, ArrivalKind.Web)]
    [InlineData(AppKind.Wasm, ArrivalKind.Web)]
    [InlineData(AppKind.Android, ArrivalKind.Store)]
    [InlineData(AppKind.Ios, ArrivalKind.Store)]
    [InlineData(AppKind.Windows, ArrivalKind.Store)]
    public void FallbackShouldDependOnTheApp(AppKind appKind, ArrivalKind expected)
        => ArrivalInfo.Fallback(appKind).Kind.Should().Be(expected);
}
```

`tests/Users.UnitTests/Usage/FunnelEventTest.cs`:

```csharp
namespace ActualChat.Users.UnitTests.Usage;

public class FunnelEventTest
{
    [Theory]
    [InlineData(FunnelEvent.JoinUsed)]
    [InlineData(FunnelEvent.SignUp)]
    [InlineData(FunnelEvent.ContactsMatched)]
    [InlineData((FunnelEvent)99)]
    public void ServerOnlyOrUnknownEventShouldNotBeClientReportable(FunnelEvent funnelEvent)
        => funnelEvent.IsClientReportable().Should().BeFalse();

    [Theory]
    [InlineData(FunnelEvent.JoinOpenedSignedOut)]
    [InlineData(FunnelEvent.InviteCopy)]
    [InlineData(FunnelEvent.ContactsAccessGranted)]
    public void ClientEventShouldBeClientReportable(FunnelEvent funnelEvent)
        => funnelEvent.IsClientReportable().Should().BeTrue();
}
```

Append to `tests/Users.UnitTests/Usage/UsageEventSourceTest.cs`, as the last methods before the private helpers:

```csharp
    [Fact]
    public void SignUpShouldCarryTheArrival()
    {
        // arrange
        var arrival = ArrivalInfo.New(ArrivalKind.Join, "inv1")!.Value;

        // act
        var signUp = UsageEventSource.SignUp(arrival, T0);

        // assert
        signUp.Kind.Should().Be(UsageEventKind.SignUp);
        signUp.SourceId.Should().Be("join:inv1");
        signUp.Value.Should().Be(1);
        signUp.Attributes!.ArrivalKind.Should().Be(ArrivalKind.Join);
        signUp.Kind.IsDayRollup().Should().BeFalse();
    }

    [Fact]
    public void OnboardingStepShouldEncodeSkipAsZero()
    {
        // act
        var skipped = UsageEventSource.OnboardingStep("Phone", false, T0);
        var completed = UsageEventSource.OnboardingStep("Phone", true, T0);

        // assert
        skipped.SourceId.Should().Be("Phone");
        skipped.Value.Should().Be(0);
        completed.Value.Should().Be(1);
        skipped.Kind.IsDayRollup().Should().BeFalse();
        UsageEventKind.Speech.IsDayRollup().Should().BeTrue();
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Users.UnitTests/Users.UnitTests.csproj --filter "FullyQualifiedName~ArrivalInfoTest|FullyQualifiedName~FunnelEventTest|FullyQualifiedName~UsageEventSourceTest"`
Expected: build FAILS (`ArrivalInfo`, `FunnelEvent`, `UsageEventKind.SignUp` not defined).

- [ ] **Step 3: Implement**

`src/dotnet/Api/Users/Usage/ArrivalKind.cs`:

```csharp
namespace ActualChat.Users;

public enum ArrivalKind
{
    Web = 0,
    Store = 1,
    Join = 2,
    User = 3,
    Campaign = 4,
}
```

`src/dotnet/Api/Users/Usage/ArrivalInfo.cs`:

```csharp
using System.Text.RegularExpressions;
using ActualChat.Hosting;

namespace ActualChat.Users;

/// <summary>
/// How a new user reached the app - the <c>SourceId</c> of their <see cref="UsageEventKind.SignUp"/> event.
/// The client carries it to sign-up in the <see cref="Constants.SessionTemporals.ArrivalKey"/> session temporal.
/// </summary>
public readonly partial record struct ArrivalInfo(ArrivalKind Kind, string Id = "")
{
    public const int MaxIdLength = 64;

    public static ArrivalInfo? New(ArrivalKind kind, string? id = null)
    {
        if (kind is ArrivalKind.Web or ArrivalKind.Store)
            return id is null ? new ArrivalInfo(kind) : null;
        if (!Enum.IsDefined(kind) || !IsValidId(id))
            return null;

        return new ArrivalInfo(kind, id!);
    }

    public static ArrivalInfo Fallback(AppKind appKind)
        => new(appKind.IsMaui() ? ArrivalKind.Store : ArrivalKind.Web);

    // Accepts a local URL ("/path?a=b"), a bare query ("a=b") or a store install referrer
    public static ArrivalInfo? FromQuery(string? urlOrQuery)
    {
        if (urlOrQuery.IsNullOrEmpty())
            return null;

        var queryStart = urlOrQuery.IndexOf('?');
        var query = queryStart >= 0 ? urlOrQuery[(queryStart + 1)..] : urlOrQuery;
        var fragmentStart = query.IndexOf('#');
        if (fragmentStart >= 0)
            query = query[..fragmentStart];
        if (!query.Contains('='))
            return null;

        var items = UriExt.GetQueryCollection(query);
        var campaign = items["utm_campaign"] ?? items["c"];
        return New(ArrivalKind.Campaign, campaign);
    }

    public static bool TryParse(string? value, out ArrivalInfo result)
    {
        result = default;
        if (value.IsNullOrEmpty())
            return false;

        var colonIndex = value.IndexOf(':');
        var prefix = colonIndex < 0 ? value : value[..colonIndex];
        var id = colonIndex < 0 ? null : value[(colonIndex + 1)..];
        if (!TryParseKind(prefix, out var kind) || New(kind, id) is not { } arrival)
            return false;

        result = arrival;
        return true;
    }

    public string Format()
        => Id.IsNullOrEmpty() ? FormatKind(Kind) : $"{FormatKind(Kind)}:{Id}";

    // Private methods

    private static bool IsValidId(string? id)
        => id is { Length: > 0 and <= MaxIdLength } && IdRegex().IsMatch(id);

    private static string FormatKind(ArrivalKind kind)
        => kind switch {
            ArrivalKind.Web => "web",
            ArrivalKind.Store => "store",
            ArrivalKind.Join => "join",
            ArrivalKind.User => "user",
            ArrivalKind.Campaign => "campaign",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static bool TryParseKind(string value, out ArrivalKind kind)
    {
        (var isParsed, kind) = value switch {
            "web" => (true, ArrivalKind.Web),
            "store" => (true, ArrivalKind.Store),
            "join" => (true, ArrivalKind.Join),
            "user" => (true, ArrivalKind.User),
            "campaign" => (true, ArrivalKind.Campaign),
            _ => (false, default),
        };
        return isParsed;
    }

    [GeneratedRegex("^[A-Za-z0-9_.@-]+$")]
    private static partial Regex IdRegex();
}
```

`src/dotnet/Api/Users/Usage/FunnelEvent.cs`:

```csharp
namespace ActualChat.Users;

/// <summary>
/// A growth-funnel step counted in the <c>usage.funnel.events</c> OTLP counter.
/// </summary>
public enum FunnelEvent
{
    JoinOpenedSignedOut = 0,
    JoinOpenedSignedIn = 1,
    JoinUsed = 2,
    UserLinkOpenedSignedOut = 3,
    UserLinkOpenedSignedIn = 4,
    SignInRequestedFromLink = 5,
    SignInCompletedFromLink = 6,
    SignUp = 7,
    InviteBannerShown = 8,
    InviteShare = 9,
    InviteCopy = 10,
    InviteQr = 11,
    ContactsAccessGranted = 12,
    ContactsMatched = 13,
}

public static class FunnelEventExt
{
    // Server-only events are counted where they happen; a client must not be able to inflate them
    public static bool IsClientReportable(this FunnelEvent funnelEvent)
        => Enum.IsDefined(funnelEvent)
            && funnelEvent is not (FunnelEvent.JoinUsed or FunnelEvent.SignUp or FunnelEvent.ContactsMatched);
}
```

`src/dotnet/Api/Users/Usage/OnboardingSteps.cs`:

```csharp
namespace ActualChat.Users;

/// <summary>
/// The <c>SourceId</c>s of <see cref="UsageEventKind.OnboardingStep"/> events, in the order the onboarding
/// modal shows them; <see cref="Finished"/> marks a completed onboarding.
/// </summary>
public static class OnboardingSteps
{
    public const string Finished = "Finished";

    public static readonly IReadOnlyList<string> All = [
        "TranscriptionTutorial",
        "PlacesTutorial",
        "SummarizationTutorial",
        "Phone",
        "Email",
        "Avatar",
        "Permissions",
        "Languages",
        "DataCollection",
        "Passkey",
        Finished,
    ];

    public static bool IsValid(string step)
        => All.Contains(step);

    public static string GetName(Type stepType)
    {
        var name = stepType.Name;
        return name.EndsWith("Step") ? name[..^4] : name;
    }
}
```

`src/dotnet/Api/Users/Usage/UsageEventKind.cs`, replaced wholesale:

```csharp
namespace ActualChat.Users;

public enum UsageEventKind
{
    Speech = 0,
    Message = 1,
    LiveSession = 2,
    Contact = 3,
    ActiveDay = 4,
    SignUp = 5,
    OnboardingStep = 6,
}

public static class UsageEventKindExt
{
    // A UsageDay row marks its day as active, so only activity kinds may create one
    public static bool IsDayRollup(this UsageEventKind kind)
        => kind is not (UsageEventKind.SignUp or UsageEventKind.OnboardingStep);
}
```

`src/dotnet/Api/Users/Usage/UsageEvent.cs`: in `UsageEventAttributes`, add after the `IsViaApi` line:

```csharp
    [DataMember, Key(4)] public ArrivalKind? ArrivalKind { get; init; }
```

`src/dotnet/Api/Constants.SessionTemporals.cs`: add after `PendingRegistrationKey`. It is not added to `ServerKeys`, because the client writes it:

```csharp
        // Client-written: how a guest reached the app, read when their account is created (see ArrivalInfo)
        public const string ArrivalKey = "Arrival";
```

`src/dotnet/Users.Contracts/UsageEventSource.cs`: add at the end of the class, after `ActiveDay`:

```csharp

    public static UsageEvent SignUp(ArrivalInfo arrival, Moment at)
        => new(UsageEventKind.SignUp, at, arrival.Format(), 1,
            new UsageEventAttributes { ArrivalKind = arrival.Kind });

    public static UsageEvent OnboardingStep(string step, bool isCompleted, Moment at)
        => new(UsageEventKind.OnboardingStep, at, step, isCompleted ? 1 : 0);
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: the same `dotnet test` command as Step 2.
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/Api/Users/Usage src/dotnet/Api/Constants.SessionTemporals.cs \
  src/dotnet/Users.Contracts/UsageEventSource.cs tests/Users.UnitTests/Usage
git commit -m "feat(usage): arrival, funnel-event and onboarding-step models

Refs #4802

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Keep the new kinds out of `UsageDay` rows

**Files:**
- Modify: `src/dotnet/Users.Service/Usage/UsageBackend.cs` (`OnRecord` loop, `OnRebuildDays` loop)
- Test: `tests/Users.IntegrationTests/UsageTest.cs`

**Interfaces:**
- Consumes: `UsageEventKindExt.IsDayRollup`, `UsageEventSource.OnboardingStep` (Task 1)

- [ ] **Step 1: Write the failing test** (add to `UsageTest`)

```csharp
    [Fact(Timeout = 90_000)]
    public async Task NonActivityKindsShouldNotCreateDayRows()
    {
        // arrange - a day nothing else touches, so any day row there comes from these events
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueAlice();
        var at = Clocks.SystemClock.Now - TimeSpan.FromDays(10);
        var range = new Range<Moment>(at - TimeSpan.FromDays(1), at + TimeSpan.FromDays(1));

        // act
        await Commander.Call(new UsageBackend_Record(account.Id, ApiArray.New(
            UsageEventSource.OnboardingStep("Phone", true, at),
            UsageEventSource.SignUp(ArrivalInfo.New(ArrivalKind.Campaign, "c1")!.Value, at))));
        var days = await Backend.ListDays(account.Id, range, default);
        await Commander.Call(new UsageBackend_RebuildDays(account.Id));
        var rebuiltDays = await Backend.ListDays(account.Id, range, default);

        // assert
        days.Should().BeEmpty("a day row counts as an active day for the review prompt");
        rebuiltDays.Should().BeEmpty("the rebuild must apply the same rule");
    }
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/Users.IntegrationTests/Users.IntegrationTests.csproj --filter "FullyQualifiedName~UsageTest.NonActivityKindsShouldNotCreateDayRows"`
Expected: FAIL. `days` contains 1 row.

- [ ] **Step 3: Implement**

In `UsageBackend.OnRecord`, inside the `foreach (var usageEvent in events)` loop, replace the block from `dbContext.Add(new DbUsageEvent(userId, usageEvent));` through `dbDay.Version = …;` with:

```csharp
            dbContext.Add(new DbUsageEvent(userId, usageEvent));
            hasChanges = true;
            UsageMeters.EventsRecorded.Add(1, new KeyValuePair<string, object?>("kind", usageEvent.Kind.ToString()));
            if (!usageEvent.Kind.IsDayRollup())
                continue;

            var day = UsageDay.DayOf(usageEvent.OccurredAt).ToDateTime();
            if (!dbDays.TryGetValue(day, out var dbDay)) {
                dbDay = new DbUsageDay { UserId = userId.Value, Day = day };
                dbDays.Add(day, dbDay);
                dbContext.Add(dbDay);
            }
            dbDay.Apply(usageEvent);
            dbDay.Version = VersionGenerator.NextVersion(dbDay.Version);
```

Also delete the now-duplicated `hasChanges = true;` and `UsageMeters.EventsRecorded.Add(...)` lines that followed the old block. Then narrow the `dayIds` query above the loop to days that need a row:

```csharp
        var dayIds = events
            .Where(e => e.Kind.IsDayRollup())
            .Select(e => UsageDay.DayOf(e.OccurredAt).ToDateTime())
            .Distinct()
            .ToList();
```

In `OnRebuildDays`, first line inside `foreach (var dbEvent in dbEvents) {`, after `var usageEvent = dbEvent.ToModel();`:

```csharp
            if (!usageEvent.Kind.IsDayRollup())
                continue;

```

- [ ] **Step 4: Run the whole `UsageTest` class to verify it passes and nothing regressed**

Run: `dotnet test tests/Users.IntegrationTests/Users.IntegrationTests.csproj --filter "FullyQualifiedName~UsageTest"`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/Users.Service/Usage/UsageBackend.cs tests/Users.IntegrationTests/UsageTest.cs
git commit -m "feat(usage): keep sign-up and onboarding events out of day rows

Refs #4802

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: `FunnelMeters` + `SignUp` event on account creation

**Files:**
- Create: `src/dotnet/Core.Server/Diagnostics/FunnelMeters.cs`
- Modify: `src/dotnet/Users.Service/AccountsBackend.cs` (`OnSignIn`, new private `RecordSignUp`)
- Test: `tests/Users.IntegrationTests/UsageTest.cs`

**Interfaces:**
- Consumes: `ArrivalInfo`, `UsageEventSource.SignUp`, `Constants.SessionTemporals.ArrivalKey`, `FunnelEvent` (Task 1)
- Produces: `static class FunnelMeters` in namespace `ActualChat.Diagnostics`, with `void Record(FunnelEvent funnelEvent, AppKind appKind, ArrivalKind? arrivalKind = null)`

- [ ] **Step 1: Write the failing tests** (add to `UsageTest`)

Add these usings at the top of the file: `using ActualChat.Users.Db;`, `using ActualLab.Fusion.EntityFramework;`, `using Microsoft.EntityFrameworkCore;`. Add these members next to `Backend`/`Usage`:

```csharp
    private DbHub<UsersDbContext> DbHub => AppHost.Services.GetRequiredService<DbHub<UsersDbContext>>();
    private ISessionTemporalsBackend SessionTemporals => AppHost.Services.GetRequiredService<ISessionTemporalsBackend>();
```

The tests, plus a private helper at the end of the class:

```csharp
    [Fact(Timeout = 90_000)]
    public async Task SignUpShouldRecordTheArrivalAndConsumeIt()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        await Commander.Call(new SessionTemporals_Set {
            Session = tester.Session, Key = Constants.SessionTemporals.ArrivalKey, Value = "join:inv-123",
        });

        // act
        var account = await tester.SignInAsUniqueAlice();

        // assert
        var signUp = await TestWait.When(async ct => {
            var events = await ListEvents(account.Id, UsageEventKind.SignUp, ct);
            events.Should().ContainSingle();
            return events[0];
        });
        signUp.SourceId.Should().Be("join:inv-123");
        signUp.Attributes!.ArrivalKind.Should().Be(ArrivalKind.Join);
        var arrivalKey = Constants.SessionTemporals.ToClientKey(Constants.SessionTemporals.ArrivalKey);
        (await SessionTemporals.Get(tester.Session, arrivalKey, default))
            .Should().BeNull("the arrival is consumed by the sign-up");
    }

    [Fact(Timeout = 90_000)]
    public async Task SignUpWithoutArrivalShouldFallBackToWeb()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);

        // act
        var account = await tester.SignInAsUniqueAlice();

        // assert
        var signUp = await TestWait.When(async ct => {
            var events = await ListEvents(account.Id, UsageEventKind.SignUp, ct);
            events.Should().ContainSingle();
            return events[0];
        });
        signUp.SourceId.Should().Be("web", "a test web client has no app user agent");
    }

    [Fact(Timeout = 90_000)]
    public async Task InvalidArrivalShouldFallBackAndNotBreakSignUp()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        await Commander.Call(new SessionTemporals_Set {
            Session = tester.Session, Key = Constants.SessionTemporals.ArrivalKey,
            Value = "join:" + new string('x', 500) + " <script>",
        });

        // act
        var account = await tester.SignInAsUniqueAlice();

        // assert
        account.IsGuestOrNull().Should().BeFalse();
        var signUp = await TestWait.When(async ct => {
            var events = await ListEvents(account.Id, UsageEventKind.SignUp, ct);
            events.Should().ContainSingle();
            return events[0];
        });
        signUp.SourceId.Should().Be("web");
    }

    [Fact(Timeout = 90_000)]
    public async Task ExistingAccountSignInShouldNotRecordSignUp()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueAlice();
        await TestWait.When(async ct => (await ListEvents(account.Id, UsageEventKind.SignUp, ct)).Should().ContainSingle());
        await using var otherTester = AppHost.NewWebClientTester(Out);
        await Commander.Call(new SessionTemporals_Set {
            Session = otherTester.Session, Key = Constants.SessionTemporals.ArrivalKey, Value = "campaign:late",
        });

        // act
        await otherTester.SignIn(account);
        await Task.Delay(500);

        // assert
        var events = await ListEvents(account.Id, UsageEventKind.SignUp, default);
        events.Should().ContainSingle().Which.SourceId.Should().Be("web", "only account creation records a sign-up");
    }

    // Private methods

    private async Task<List<UsageEvent>> ListEvents(
        UserId userId, UsageEventKind kind, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken);
        await using var _ = dbContext;
        var dbEvents = await dbContext.UsageEvents
            .Where(e => e.UserId == userId.Value && e.Kind == kind)
            .ToListAsync(cancellationToken);
        return dbEvents.Select(e => e.ToModel()).ToList();
    }
```

If `UsageTest` already has a `// Private methods` section, put `ListEvents` there. `otherTester.SignIn(account)` reuses the first identity, because `SignIn` takes `account.Identities.Keys.First()`. If that identity turns out to be empty on the returned account, sign in a second time on the same `tester` instead; the `TestAuthExt.SignIn` path signs out first.

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/Users.IntegrationTests/Users.IntegrationTests.csproj --filter "FullyQualifiedName~UsageTest.SignUp|FullyQualifiedName~UsageTest.InvalidArrival|FullyQualifiedName~UsageTest.ExistingAccount"`
Expected: FAIL. There are no `SignUp` rows (the `TestWait.When` calls time out).

- [ ] **Step 3: Implement**

`src/dotnet/Core.Server/Diagnostics/FunnelMeters.cs`:

```csharp
using System.Diagnostics;
using System.Diagnostics.Metrics;
using ActualChat.Hosting;
using ActualChat.Users;

namespace ActualChat.Diagnostics;

public static class FunnelMeters
{
    public static readonly Counter<long> Events;

    static FunnelMeters()
    {
        var m = CoreServerInstruments.Meter;
        Events = m.CreateCounter<long>(
            "usage.funnel.events", null, "Growth funnel steps, by event and app");
    }

    public static void Record(FunnelEvent funnelEvent, AppKind appKind, ArrivalKind? arrivalKind = null)
    {
        var tags = new TagList {
            { "event", funnelEvent.ToString() },
            { "app", appKind.ToString() },
        };
        if (arrivalKind is { } vArrivalKind)
            tags.Add("arrival", vArrivalKind.ToString());
        Events.Add(1, tags);
    }
}
```

Check that `CoreServerInstruments` is in `ActualChat.Diagnostics` (`UsageMeters` uses it via `using ActualChat.Diagnostics;`). If it is in another namespace, add that using.

`src/dotnet/Users.Service/AccountsBackend.cs`:

1. Add these DI properties next to `SessionsBackend`:

```csharp
    private ISessionTemporalsBackend SessionTemporalsBackend => field ??= Services.GetRequiredService<ISessionTemporalsBackend>();
```

2. In `OnSignIn`, inside `if (isNew) {`, right after `context.Operation.AddEvent(new NewAccountEvent(userId));`:

```csharp
            if (!account.IsBot)
                await RecordSignUp(context, session, userId, sessionInfo, cancellationToken).ConfigureAwait(false);
```

3. Add a private method in the `// Private methods` section. Put it first in that section if `OnSignIn` is the only caller:

```csharp
    private async Task RecordSignUp(
        CommandContext context, Session session, UserId userId, SessionInfo? sessionInfo,
        CancellationToken cancellationToken)
    {
        // Measurement only: whatever goes wrong here must not fail the sign-in
        try {
            var arrivalKey = Constants.SessionTemporals.ToClientKey(Constants.SessionTemporals.ArrivalKey);
            var value = await SessionTemporalsBackend.Get(session, arrivalKey, cancellationToken).ConfigureAwait(false);
            AppKindExt.TryParseUserAgent(sessionInfo?.Description, out var appKind);
            if (!ArrivalInfo.TryParse(value, out var arrival))
                arrival = ArrivalInfo.Fallback(appKind);

            var signUp = UsageEventSource.SignUp(arrival, Clocks.SystemClock.Now);
            context.Operation.AddEvent(new UsageBackend_Record(userId, ApiArray.New(signUp)));
            FunnelMeters.Record(FunnelEvent.SignUp, appKind, arrival.Kind);
            if (value is not null)
                await Commander
                    .Call(new SessionTemporalsBackend_Set(session, arrivalKey, null), true, cancellationToken)
                    .ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "Failed to record the sign-up of user '{UserId}'", userId);
        }
    }
```

Then match the actual types. If `sessionInfo`'s declared type in `OnSignIn` is `SessionInfo?` from `SessionsBackend.Get`, keep the parameter as-is; otherwise use its real type. Add `using ActualChat.Diagnostics;` and `using ActualChat.Hosting;` if they are not global usings (check `src/dotnet/Directory.Build.props`). Use the class's existing `Log` and `Commander` members; if `Log` doesn't exist, add `private ILogger Log => field ??= Services.LogFor(GetType());`.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Users.IntegrationTests/Users.IntegrationTests.csproj --filter "FullyQualifiedName~UsageTest|FullyQualifiedName~PendingRegistrationTest"`
Expected: all PASS. `PendingRegistrationTest` covers the confirm-register path, which also reaches `isNew`.

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/Core.Server/Diagnostics/FunnelMeters.cs src/dotnet/Users.Service/AccountsBackend.cs \
  tests/Users.IntegrationTests/UsageTest.cs
git commit -m "feat(usage): record the arrival path as a SignUp event on account creation

Refs #4802

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: `IUsage` commands for onboarding steps and funnel events, plus server-side counters

**Files:**
- Modify: `src/dotnet/Api.Contracts/Users/IUsage.cs`
- Modify: `src/dotnet/Users.Service/Usage/Usage.cs`
- Modify: `src/dotnet/Invite.Service/InvitesBackend.cs` (`OnUse`, after `SaveChangesAsync`)
- Modify: `src/dotnet/Contacts.Service/ContactLinker.cs` (`EnsureContactExists`, inside `if (!contact.IsRegular)`)
- Test: `tests/Users.IntegrationTests/UsageTest.cs`

**Interfaces:**
- Consumes: `OnboardingSteps`, `FunnelEvent.IsClientReportable`, `UsageEventSource.OnboardingStep` (Task 1), `FunnelMeters.Record` (Task 3)
- Produces:
  - `Usage_RecordOnboardingStep { Session, Step (string, Key 2), IsCompleted (bool, Key 3) }`
  - `Usage_RecordFunnelEvent { Session, Event (FunnelEvent, Key 2) }`
  - Both are `ApiCommand<Unit>` in namespace `ActualChat.Users`.

- [ ] **Step 1: Write the failing tests** (add to `UsageTest`)

```csharp
    [Fact(Timeout = 90_000)]
    public async Task OnboardingStepShouldBeRecordedOncePerStep()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueAlice();

        // act
        await Commander.Call(new Usage_RecordOnboardingStep { Session = tester.Session, Step = "Phone", IsCompleted = false });
        await Commander.Call(new Usage_RecordOnboardingStep { Session = tester.Session, Step = "Phone", IsCompleted = true });
        await Commander.Call(new Usage_RecordOnboardingStep {
            Session = tester.Session, Step = OnboardingSteps.Finished, IsCompleted = true,
        });

        // assert
        var events = await ListEvents(account.Id, UsageEventKind.OnboardingStep, default);
        events.Should().HaveCount(2);
        events.Single(e => e.SourceId == "Phone").Value.Should().Be(0, "the first report of a step wins");
        events.Should().Contain(e => e.SourceId == OnboardingSteps.Finished);
    }

    [Fact(Timeout = 90_000)]
    public async Task UnknownOrGuestOnboardingStepShouldBeRejected()
    {
        // arrange
        await using var guestTester = AppHost.NewWebClientTester(Out);
        await using var tester = AppHost.NewWebClientTester(Out);
        await tester.SignInAsUniqueAlice();

        // act
        var unknownStep = () => Commander.Call(
            new Usage_RecordOnboardingStep { Session = tester.Session, Step = "Nope", IsCompleted = true });
        var guestStep = () => Commander.Call(
            new Usage_RecordOnboardingStep { Session = guestTester.Session, Step = "Phone", IsCompleted = true });

        // assert
        await unknownStep.Should().ThrowAsync<Exception>();
        await guestStep.Should().ThrowAsync<Exception>();
    }

    [Fact(Timeout = 90_000)]
    public async Task FunnelEventShouldAcceptOnlyClientEvents()
    {
        // arrange - a guest on purpose: link-opened events happen before an account exists
        await using var guestTester = AppHost.NewWebClientTester(Out);

        // act
        var clientEvent = () => Commander.Call(
            new Usage_RecordFunnelEvent { Session = guestTester.Session, Event = FunnelEvent.JoinOpenedSignedOut });
        var serverEvent = () => Commander.Call(
            new Usage_RecordFunnelEvent { Session = guestTester.Session, Event = FunnelEvent.SignUp });
        var unknownEvent = () => Commander.Call(
            new Usage_RecordFunnelEvent { Session = guestTester.Session, Event = (FunnelEvent)99 });

        // assert
        await clientEvent.Should().NotThrowAsync();
        await serverEvent.Should().ThrowAsync<Exception>();
        await unknownEvent.Should().ThrowAsync<Exception>();
    }
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/Users.IntegrationTests/Users.IntegrationTests.csproj --filter "FullyQualifiedName~UsageTest.OnboardingStep|FullyQualifiedName~UsageTest.UnknownOrGuest|FullyQualifiedName~UsageTest.FunnelEvent"`
Expected: build FAILS (the commands are not defined).

- [ ] **Step 3: Implement**

`src/dotnet/Api.Contracts/Users/IUsage.cs`: add to the interface after `OnRebuildOwnDays`:

```csharp
    [CommandHandler]
    Task OnRecordOnboardingStep(Usage_RecordOnboardingStep command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRecordFunnelEvent(Usage_RecordFunnelEvent command, CancellationToken cancellationToken);
```

Add the records after `Usage_RebuildOwnDays`:

```csharp

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Usage_RecordOnboardingStep : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required string Step { get; init; }
    [DataMember(Order = 3), Key(3)] public required bool IsCompleted { get; init; }
}

/// <summary>
/// Counts a funnel step only the client sees; guests may send it.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Usage_RecordFunnelEvent : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required FunnelEvent Event { get; init; }
}
```

`src/dotnet/Users.Service/Usage/Usage.cs`: add after `OnRebuildOwnDays`, in the same order as the interface:

```csharp

    // [CommandHandler]
    public virtual async Task OnRecordOnboardingStep(
        Usage_RecordOnboardingStep command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        if (!OnboardingSteps.IsValid(command.Step))
            throw StandardError.Constraint($"Unknown onboarding step: '{command.Step}'.");

        var account = await Accounts.GetOwn(command.Session, cancellationToken).ConfigureAwait(false);
        account.Require(AccountFull.MustNotBeGuest);
        var usageEvent = UsageEventSource.OnboardingStep(command.Step, command.IsCompleted, Clocks.SystemClock.Now);
        await Commander
            .Call(new UsageBackend_Record(account.Id, ApiArray.New(usageEvent)), true, cancellationToken)
            .ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnRecordFunnelEvent(Usage_RecordFunnelEvent command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var funnelEvent = command.Event;
        if (!funnelEvent.IsClientReportable())
            throw StandardError.Constraint($"Funnel event '{funnelEvent}' can't be reported by a client.");

        var sessionInfo = await Accounts.GetSessionInfo(command.Session, cancellationToken).ConfigureAwait(false);
        AppKindExt.TryParseUserAgent(sessionInfo?.Description, out var appKind);
        FunnelMeters.Record(funnelEvent, appKind);
    }
```

Add `using ActualChat.Diagnostics;` if it is not already present or global.

`src/dotnet/Invite.Service/InvitesBackend.cs` `OnUse`: between `await dbContext.SaveChangesAsync(...)` and `context.Operation.Items.KeylessSet(invite);` insert:

```csharp
        var sessionInfo = await Accounts.GetSessionInfo(command.Session, cancellationToken).ConfigureAwait(false);
        AppKindExt.TryParseUserAgent(sessionInfo?.Description, out var appKind);
        FunnelMeters.Record(FunnelEvent.JoinUsed, appKind);
```

Add `using ActualChat.Diagnostics;` and `using ActualChat.Users;` if needed. Check that `Invite.Service` can reach `FunnelMeters`, i.e. that it references `Core.Server` through `Db`, by building:
`dotnet build src/dotnet/Invite.Service/Invite.Service.csproj`.

`src/dotnet/Contacts.Service/ContactLinker.cs` `EnsureContactExists`: inside `if (!contact.IsRegular) {`, after the `await Commander.Call(createCmd, ...)` line:

```csharp
            FunnelMeters.Record(FunnelEvent.ContactsMatched, AppKind.Unknown);
```

The same using caveats apply. `EnsureContactExists` runs in a background linker without a session, so the app is `Unknown`.

- [ ] **Step 4: Run the tests to verify they pass, and build the touched services**

Run: `dotnet build src/dotnet/Contacts.Service/Contacts.Service.csproj && dotnet test tests/Users.IntegrationTests/Users.IntegrationTests.csproj --filter "FullyQualifiedName~UsageTest"`
Expected: the build succeeds and all tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/Api.Contracts/Users/IUsage.cs src/dotnet/Users.Service/Usage/Usage.cs \
  src/dotnet/Invite.Service/InvitesBackend.cs src/dotnet/Contacts.Service/ContactLinker.cs \
  tests/Users.IntegrationTests/UsageTest.cs
git commit -m "feat(usage): onboarding-step and funnel-event commands, server funnel counters

Refs #4802

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Client — funnel helper, arrival capture, link pages, sign-in from link

**Files:**
- Create: `src/dotnet/UI.Blazor/Services/UsageUIExt.cs`
- Create: `src/dotnet/UI.Blazor/Services/IInstallReferrer.cs`
- Create: `src/dotnet/UI.Blazor/Services/AccountUI/AccountUI.Arrival.cs`
- Modify: `src/dotnet/UI.Blazor/Services/AccountUI/AccountUI.StateSync.cs` (`OnRun` chains)
- Modify: `src/dotnet/UI.Blazor/Services/AccountUI/AccountUI.cs` (`RequestSignInFromHomePage`)
- Modify: `src/dotnet/UI.Blazor.App/Pages/ChatInvitePage.razor`
- Modify: `src/dotnet/UI.Blazor.App/Pages/UserPage.razor`

**Interfaces:**
- Consumes: `ArrivalInfo`, `FunnelEvent`, `Constants.SessionTemporals.ArrivalKey` (Task 1); `Usage_RecordFunnelEvent` (Task 4)
- Produces:
  - `UsageUIExt.RecordFunnelEvent(this UIHub hub, FunnelEvent funnelEvent)`, fire-and-forget
  - `AccountUI.SetArrival(ArrivalInfo arrival, CancellationToken cancellationToken = default) : Task`
  - `interface IInstallReferrer { Task<string?> GetQuery(CancellationToken cancellationToken); }`

No automated test covers this task. The server-side contract it feeds is already pinned by Tasks 3–4. The manual pass in Task 9 exercises it end to end.

- [ ] **Step 1: Funnel helper and install-referrer seam**

`src/dotnet/UI.Blazor/Services/UsageUIExt.cs`:

```csharp
namespace ActualChat.UI.Blazor.Services;

public static class UsageUIExt
{
    // Fire-and-forget: a lost count must never surface in the UI
    public static void RecordFunnelEvent(this UIHub hub, FunnelEvent funnelEvent)
        => _ = hub.Commander
            .Call(new Usage_RecordFunnelEvent { Session = hub.Session, Event = funnelEvent }, CancellationToken.None)
            .ContinueWith(
                t => hub.Services.LogFor(typeof(UsageUIExt))
                    .LogDebug(t.Exception, "Failed to record funnel event {Event}", funnelEvent),
                TaskContinuationOptions.OnlyOnFaulted);
}
```

`src/dotnet/UI.Blazor/Services/IInstallReferrer.cs`:

```csharp
namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// The store's install referrer query (<c>utm_source=...&amp;utm_campaign=...</c>);
/// registered only on platforms whose store provides one.
/// </summary>
public interface IInstallReferrer
{
    Task<string?> GetQuery(CancellationToken cancellationToken);
}
```

Add `using ActualChat.Users;` to `UsageUIExt.cs` if `ActualChat.Users` is not a global using in UI.Blazor.

- [ ] **Step 2: Arrival capture in `AccountUI`**

`src/dotnet/UI.Blazor/Services/AccountUI/AccountUI.Arrival.cs`:

```csharp
namespace ActualChat.UI.Blazor.Services;

public partial class AccountUI
{
    private ISessionTemporals SessionTemporals => Hub.SessionTemporals;

    // Overwrites: the last link a guest opened is the one that led them to sign up
    public async Task SetArrival(ArrivalInfo arrival, CancellationToken cancellationToken = default)
    {
        try {
            await WhenReady.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (!OwnAccount.Value.IsGuestOrNull())
                return;

            var command = new SessionTemporals_Set {
                Session = Session,
                Key = Constants.SessionTemporals.ArrivalKey,
                Value = arrival.Format(),
            };
            await Commander.Call(command, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "Failed to set the arrival to {Arrival}", arrival.Format());
        }
    }

    // Private methods

    // Fills only an empty arrival: a link page that already ran must win over the landing URL
    private async Task CaptureLandingArrival(CancellationToken cancellationToken)
    {
        try {
            await WhenReady.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (!OwnAccount.Value.IsGuestOrNull())
                return;

            var existing = await SessionTemporals
                .Get(Session, Constants.SessionTemporals.ArrivalKey, cancellationToken)
                .ConfigureAwait(false);
            if (existing is not null)
                return;

            var arrival = ArrivalInfo.FromQuery(History.DefaultItem.Url);
            if (arrival is null && Services.GetService<IInstallReferrer>() is { } installReferrer) {
                var query = await installReferrer.GetQuery(cancellationToken).ConfigureAwait(false);
                arrival = ArrivalInfo.FromQuery(query);
            }
            if (arrival is { } vArrival)
                await SetArrival(vArrival, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "Failed to capture the landing arrival");
        }
    }
}
```

`GetService` is deliberate here: only Android registers `IInstallReferrer`, the same pattern `PermissionsUI` uses for `BatteryOptimizationHandler`. Check that `AccountUI` has `History`, `Commander`, `Session` and `Log`. It is a `UIWorkerBase<UIHub>`, and `RequestSignInFromHomePage` already uses `History`. If `Commander` isn't a member, use `Hub.Commander`. If `History.DefaultItem.Url` is not a `string`, use `.Value`.

`AccountUI.StateSync.cs` `OnRun`: add `AsyncChain.From(CaptureLandingArrival),` as the last entry of `chains`. It swallows its own errors and completes, so `RetryForever` never re-runs it.

- [ ] **Step 3: Sign-in requested/completed from a link**

In `AccountUI.RequestSignInFromHomePage`, add as the first lines of the method:

```csharp
        var isFromLink = redirectUrl is not null
            && (new LocalUrl(redirectUrl).IsPrivateChatInvite() || new LocalUrl(redirectUrl).IsUser());
        if (isFromLink)
            Hub.RecordFunnelEvent(FunnelEvent.SignInRequestedFromLink);
```

Replace the final `return OwnAccount.Value is { IsGuest: false };` with:

```csharp
        var isSignedIn = OwnAccount.Value is { IsGuest: false };
        if (isFromLink && isSignedIn)
            Hub.RecordFunnelEvent(FunnelEvent.SignInCompletedFromLink);
        return isSignedIn;
```

- [ ] **Step 4: Link pages**

`ChatInvitePage.razor` `@code`: add the field `private string? _reportedInviteId;` and replace the start of `OnParametersSetAsync` up to and including the guest `return;` with:

```csharp
    protected override async Task OnParametersSetAsync() {
        _account = await AccountUI.OwnAccount.Use();
        var isSignedOut = _account.IsGuest || !_account.IsActive();
        var mustReport = _reportedInviteId != InviteId;
        _reportedInviteId = InviteId;
        if (isSignedOut) {
            _signInFromHomeRequested = true;
            if (mustReport) {
                Hub.RecordFunnelEvent(FunnelEvent.JoinOpenedSignedOut);
                if (ArrivalInfo.New(ArrivalKind.Join, InviteId) is { } arrival)
                    _ = AccountUI.SetArrival(arrival);
            }
            _ = AccountUI.RequestSignInFromHomePage(L.SignIn_ToUseChatInvite, History.LocalUrl);
            return;
        }

        if (mustReport)
            Hub.RecordFunnelEvent(FunnelEvent.JoinOpenedSignedIn);
```

The rest of the method stays unchanged. `_reportedInviteId` stops a parent re-render from counting the same open twice.

`UserPage.razor` `@code`: add `private UserId? _reportedUserId;`. Replace the tail, from `var chatId = PeerChatId.New(...)`, with:

```csharp
        if (_reportedUserId != account.Id) {
            _reportedUserId = account.Id;
            if (ownAccount.IsGuestOrNull()) {
                Hub.RecordFunnelEvent(FunnelEvent.UserLinkOpenedSignedOut);
                if (ArrivalInfo.New(ArrivalKind.User, account.Id.Value) is { } arrival)
                    _ = AccountUI.SetArrival(arrival);
            }
            else
                Hub.RecordFunnelEvent(FunnelEvent.UserLinkOpenedSignedIn);
        }

        var chatId = PeerChatId.New(ownAccount.Id, account.Id);
        _ = History.NavigateTo(Links.Chat(chatId), true);
```

Add `@using ActualChat.Users` at the top of each page if the namespace isn't already imported (check `_Imports.razor`).

- [ ] **Step 5: Build**

Run: `dotnet build src/dotnet/UI.Blazor.App/UI.Blazor.App.csproj`
Expected: success, with no new warnings in the touched files.

- [ ] **Step 6: Commit**

```bash
git add src/dotnet/UI.Blazor/Services src/dotnet/UI.Blazor.App/Pages/ChatInvitePage.razor \
  src/dotnet/UI.Blazor.App/Pages/UserPage.razor
git commit -m "feat(usage): capture the arrival path and link funnel events on the client

Refs #4802

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Client — onboarding step reporting

**Files:**
- Create: `src/dotnet/UI.Blazor/Components/Stepper/StepFinishedArgs.cs`
- Modify: `src/dotnet/UI.Blazor/Components/Stepper/Stepper.razor` (`TryMoveForward`, `Skip`, new parameter)
- Modify: `src/dotnet/UI.Blazor.App/Components/Onboarding/OnboardingModal.razor`
- Test: `tests/Chat.UI.Blazor.UnitTests/OnboardingStepsTest.cs` (create)

**Interfaces:**
- Consumes: `OnboardingSteps` (Task 1), `Usage_RecordOnboardingStep` (Task 4)
- Produces: `sealed record StepFinishedArgs(IStep Step, bool IsSkipped)` and `Stepper.StepFinished` (`EventCallback<StepFinishedArgs>`)

- [ ] **Step 1: Write the failing test**

`tests/Chat.UI.Blazor.UnitTests/OnboardingStepsTest.cs`:

```csharp
using ActualChat.UI.Blazor.App.Components;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class OnboardingStepsTest
{
    [Fact]
    public void OnboardingStepNamesShouldMatchModalSteps()
    {
        // arrange - the steps OnboardingModal renders, in its order; a rename must fail here, not drop data
        var stepTypes = new[] {
            typeof(TranscriptionTutorialStep),
            typeof(PlacesTutorialStep),
            typeof(SummarizationTutorialStep),
            typeof(PhoneStep),
            typeof(EmailStep),
            typeof(AvatarStep),
            typeof(PermissionsStep),
            typeof(LanguagesStep),
            typeof(DataCollectionStep),
            typeof(PasskeyStep),
        };

        // act
        var names = stepTypes.Select(OnboardingSteps.GetName).Append(OnboardingSteps.Finished).ToList();

        // assert
        names.Should().Equal(OnboardingSteps.All);
    }
}
```

Match the namespace of the other test files in that project; if their namespace differs, use theirs. The step types' namespace is whatever `OnboardingModal.razor` resolves them from; check with `grep -n "@namespace" src/dotnet/UI.Blazor.App/Components/Onboarding/*.razor`.

- [ ] **Step 2: Run it**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj --filter "FullyQualifiedName~OnboardingStepsTest"`
Expected: PASS. It pins the Task 1 list against the real types. If it fails, fix `OnboardingSteps.All`, not the test.

- [ ] **Step 3: Stepper callback**

`src/dotnet/UI.Blazor/Components/Stepper/StepFinishedArgs.cs`:

```csharp
namespace ActualChat.UI.Blazor.Components;

public sealed record StepFinishedArgs(IStep Step, bool IsSkipped);
```

`Stepper.razor` `@code`: add `[Parameter] public EventCallback<StepFinishedArgs> StepFinished { get; set; }` after `CurrentStepChanged`. Replace `TryMoveForward` and `Skip` with:

```csharp
    public async ValueTask<bool> TryMoveForward() {
        if (_currentStep is not { } step || !await step.TryComplete())
            return false;

        await StepFinished.InvokeAsync(new StepFinishedArgs(step, false));
        return await Move(1);
    }

    public async ValueTask Skip() {
        if (_currentStep is not { } step)
            return;

        await step.Skip();
        await StepFinished.InvokeAsync(new StepFinishedArgs(step, true));
        await Move(1);
    }
```

- [ ] **Step 4: Onboarding modal**

In `OnboardingModal.razor`, add `StepFinished="@OnStepFinished"` to the `<Stepper …>` tag. In `@code`:

- Add `private ILogger Log => field ??= Hub.LogFor(GetType());` next to the other private properties.
- In `OnCurrentStepChanged`, after the `if (!isCompleted) return;` guard, add `RecordStep(OnboardingSteps.Finished, true);`.
- Add these under `// Event handlers`:

```csharp
    private void OnStepFinished(StepFinishedArgs args)
        => RecordStep(OnboardingSteps.GetName(args.Step.GetType()), !args.IsSkipped);
```

- Add under a `// Private methods` section, placed before `// Nested types`:

```csharp
    private void RecordStep(string step, bool isCompleted) {
        if (!OnboardingSteps.IsValid(step)) {
            Log.LogWarning("RecordStep: unknown onboarding step {Step}", step);
            return;
        }

        var command = new Usage_RecordOnboardingStep { Session = Session, Step = step, IsCompleted = isCompleted };
        _ = Hub.Commander.Call(command, CancellationToken.None)
            .ContinueWith(t => Log.LogWarning(t.Exception, "RecordStep: failed for {Step}", step),
                TaskContinuationOptions.OnlyOnFaulted);
    }
```

The brace style is K&R because this is razor code.

- [ ] **Step 5: Build and re-run the test**

Run: `dotnet build src/dotnet/UI.Blazor.App/UI.Blazor.App.csproj && dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj --filter "FullyQualifiedName~OnboardingStepsTest"`
Expected: success and PASS.

- [ ] **Step 6: Commit**

```bash
git add src/dotnet/UI.Blazor/Components/Stepper src/dotnet/UI.Blazor.App/Components/Onboarding/OnboardingModal.razor \
  tests/Chat.UI.Blazor.UnitTests/OnboardingStepsTest.cs
git commit -m "feat(usage): report onboarding steps completed or skipped

Refs #4802

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Client — invite banner, share taps, contacts access

**Files:**
- Create: `src/dotnet/UI.Blazor.App/Components/Share/ShareActionKind.cs`
- Modify: `src/dotnet/UI.Blazor.App/Components/Share/ShareActions.razor`
- Modify: `src/dotnet/UI.Blazor.App/Components/ChatList/InviteFriendsBanner.razor`
- Modify: `src/dotnet/UI.Blazor.App/Services/ChatListUI.cs`
- Modify: `src/dotnet/UI.Blazor/Services/Permissions/PermissionHandler.cs` (`CheckOrRequest`, new virtual)
- Modify: `src/dotnet/UI.Blazor/Services/Permissions/ContactsPermissionHandler.cs`

**Interfaces:**
- Consumes: `UsageUIExt.RecordFunnelEvent` (Task 5), `FunnelEvent` (Task 1)
- Produces:
  - `enum ShareActionKind { Share, Copy, Qr }` in namespace `ActualChat.UI.Blazor.App.Components`
  - `ShareActions.Tapped` (`Action<ShareActionKind>?`)
  - `ChatListUI.ReportInviteBannerShown()`
  - `PermissionHandler.OnRequestGranted()` (protected virtual)

No automated test covers this task: these are pure UI wiring into the Task 4 command. Task 9 verifies it manually.

- [ ] **Step 1: Share taps**

`src/dotnet/UI.Blazor.App/Components/Share/ShareActionKind.cs`:

```csharp
namespace ActualChat.UI.Blazor.App.Components;

public enum ShareActionKind
{
    Share,
    Copy,
    Qr,
}
```

`ShareActions.razor` markup: wrap the share button and both `CopyTrigger`s. The JS handlers in `copy-trigger.ts` and `share.ts` never stop propagation, so the click bubbles to Blazor, and `contents` (Tailwind `display: contents`) keeps them direct flex items of `.share-actions`:

```razor
<div class="share-actions">
    @if (_canShareExternally) {
        <span class="contents" @onclick="@(() => Tapped?.Invoke(ShareActionKind.Share))">
            <ShareExternallyButton ButtonClass="btn-square" Request="@request"/>
        </span>
        <span class="contents" @onclick="@(() => Tapped?.Invoke(ShareActionKind.Copy))">
            <CopyTrigger Tooltip="@L.Share_CopyLink" CopyText="@link">
                ...unchanged children...
            </CopyTrigger>
        </span>
    } else {
        <span class="contents" @onclick="@(() => Tapped?.Invoke(ShareActionKind.Copy))">
            <CopyTrigger Class="c-copy-link" CopyText="@link">
                ...unchanged children...
            </CopyTrigger>
        </span>
    }
    ...QR button unchanged...
</div>
```

(`...unchanged children...` means keep the existing inner markup exactly as it is.)

`@code`: add `[Parameter] public Action<ShareActionKind>? Tapped { get; set; }` after `ScanHandler`. Make `OnShowQrClick` a block body:

```csharp
    private Task OnShowQrClick() {
        Tapped?.Invoke(ShareActionKind.Qr);
        return ModalUI.Show(new ShareQrModalModel(
            Model.Title,
            Model.Request.Link.GetValueOrDefault(),
            Model.ImageUrl,
            ScanHandler) {
            AbsoluteUrl = Model.Request.HasExternalLink() ? Model.Request.GetShareLink(UrlMapper) : null,
        });
    }
```

Also remove the double blank line after `private bool _canShareExternally;`, which the style hook would flag.

- [ ] **Step 2: Banner shown and taps**

`ChatListUI.cs`: add a private field `private int _isInviteBannerShownReported;` with the other instance fields, and a public method next to `MustShowInviteFriendsBanner`:

```csharp
    // Once per app run: the virtual list re-creates the banner as it scrolls in and out
    public void ReportInviteBannerShown()
    {
        if (Interlocked.Exchange(ref _isInviteBannerShownReported, 1) == 0)
            Hub.RecordFunnelEvent(FunnelEvent.InviteBannerShown);
    }
```

`InviteFriendsBanner.razor`: pass the tap handler, `<ShareActions Model="@m.ShareModel" Tapped="@OnShareTapped"/>`. In `@code` add:

```csharp
    protected override void OnInitialized() {
        base.OnInitialized();
        ChatListUI.ReportInviteBannerShown();
    }

    private void OnShareTapped(ShareActionKind kind)
        => Hub.RecordFunnelEvent(kind switch {
            ShareActionKind.Share => FunnelEvent.InviteShare,
            ShareActionKind.Copy => FunnelEvent.InviteCopy,
            _ => FunnelEvent.InviteQr,
        });
```

If `ChatListUI` isn't reachable as a member in the component, add `private ChatListUI ChatListUI => Hub.ChatListUI;`. If `OnInitialized` is sealed on `ComputedStateComponent`, report from `OnAfterRender(bool firstRender)` when `firstRender` is true instead.

- [ ] **Step 3: Contacts access granted**

`PermissionHandler.CheckOrRequest`: inside the dispatcher lambda, replace the final two lines:

```csharp
            isGranted = await Get(cancellationToken).ConfigureAwait(false);
            SetUnsafe(isGranted);
            return isGranted ?? false;
```

with:

```csharp
            isGranted = await Get(cancellationToken).ConfigureAwait(false);
            SetUnsafe(isGranted);
            if (isGranted == true)
                OnRequestGranted();
            return isGranted ?? false;
```

Add a protected virtual in the `// Protected methods` section, after the abstract methods:

```csharp
    // Called only when our own request, not an earlier grant, made the permission granted
    protected virtual void OnRequestGranted()
    { }
```

`ContactsPermissionHandler.cs`:

```csharp
namespace ActualChat.UI.Blazor.Services;

public abstract class ContactsPermissionHandler(UIHub hub, bool mustStart = true)
    : PermissionHandler(hub, mustStart)
{
    protected override void OnRequestGranted()
        => Hub.RecordFunnelEvent(FunnelEvent.ContactsAccessGranted);
}
```

- [ ] **Step 4: Build**

Run: `dotnet build src/dotnet/UI.Blazor.App/UI.Blazor.App.csproj`
Expected: success.

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/UI.Blazor.App/Components/Share src/dotnet/UI.Blazor.App/Components/ChatList/InviteFriendsBanner.razor \
  src/dotnet/UI.Blazor.App/Services/ChatListUI.cs src/dotnet/UI.Blazor/Services/Permissions
git commit -m "feat(usage): count invite-banner impressions, share taps and contacts access

Refs #4802

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: Android Play Install Referrer

**Files:**
- Modify: `Directory.Packages.props` (repo root; add the package version next to `Xamarin.Google.Android.Play.Review`)
- Modify: `src/dotnet/App.Maui/App.Maui.csproj` (add the `PackageReference` next to `Xamarin.Google.Android.Play.Review`, line ~196, in the same Android-only item group)
- Create: `src/dotnet/App.Maui/Platforms/Android/AndroidInstallReferrer.cs`
- Modify: `src/dotnet/App.Maui/MauiProgram.Android.cs` (next to `IAppReviewer` at line ~43)

**Interfaces:**
- Consumes: `IInstallReferrer` (Task 5)

- [ ] **Step 1: Package**

`Directory.Packages.props`: `<PackageVersion Include="Xamarin.Google.Android.InstallReferrer" Version="2.2.0.9" />`
`App.Maui.csproj`: `<PackageReference Include="Xamarin.Google.Android.InstallReferrer" />`

- [ ] **Step 2: Implementation**

`src/dotnet/App.Maui/Platforms/Android/AndroidInstallReferrer.cs`:

```csharp
using ActualChat.UI.Blazor.Services;
using Com.Android.Installreferrer.Api;

namespace ActualChat.App.Maui;

public sealed class AndroidInstallReferrer : IInstallReferrer
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

    public async Task<string?> GetQuery(CancellationToken cancellationToken)
    {
        var client = InstallReferrerClient.NewBuilder(Platform.AppContext).Build();
        var listener = new StateListener();
        client.StartConnection(listener);
        try {
            var responseCode = await listener.WhenSetUp
                .WaitAsync(ConnectTimeout, cancellationToken)
                .ConfigureAwait(false);
            if (responseCode != InstallReferrerClient.InstallReferrerResponse.Ok)
                return null;

            return client.InstallReferrer.InstallReferrer;
        }
        finally {
            client.EndConnection();
        }
    }

    // Nested types

    private sealed class StateListener : Java.Lang.Object, IInstallReferrerStateListener
    {
        private readonly TaskCompletionSource<int> _whenSetUp = TaskCompletionSourceExt.New<int>();

        public Task<int> WhenSetUp => _whenSetUp.Task;

        public void OnInstallReferrerSetupFinished(int responseCode)
            => _whenSetUp.TrySetResult(responseCode);

        public void OnInstallReferrerServiceDisconnected()
            => _whenSetUp.TrySetResult(InstallReferrerClient.InstallReferrerResponse.ServiceDisconnected);
    }
}
```

The binding's names are expected, not verified: the namespace `Com.Android.Installreferrer.Api`, `InstallReferrerClient.InstallReferrerResponse.*` constants, and `ReferrerDetails.InstallReferrer`. Confirm them in Step 4's build. If a name differs, find the real one with
`find ~/.nuget/packages/xamarin.google.android.installreferrer -name "*.dll" | head -1 | xargs -I{} dotnet ildasm {} 2>/dev/null | grep -i referrer | head`, or read the package's `api.xml`. Keep the shape.

`MauiProgram.Android.cs`: next to the `IAppReviewer` registration add
`services.AddScoped<IInstallReferrer>(_ => new AndroidInstallReferrer());`

- [ ] **Step 3: Check the Android target framework name**

Run: `grep -n "TargetFrameworks" src/dotnet/App.Maui/App.Maui.csproj`
Use the `-android` TFM it lists in Step 4.

- [ ] **Step 4: Build Android**

Run: `dotnet build src/dotnet/App.Maui/App.Maui.csproj -f <android TFM> -c Debug`
Expected: success. The maui-android workload is installed in WSL. Fix any binding-name mismatches as described in Step 2.

- [ ] **Step 5: Commit**

```bash
git add Directory.Packages.props src/dotnet/App.Maui/App.Maui.csproj \
  src/dotnet/App.Maui/Platforms/Android/AndroidInstallReferrer.cs src/dotnet/App.Maui/MauiProgram.Android.cs
git commit -m "feat(usage): read the Play install referrer as the arrival campaign on Android

Refs #4802

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 9: AOT sources, full build, tests, and a manual pass

**Files:**
- Modify (generated): `src/dotnet/*/Module/*AotSource.g.cs`, `src/dotnet/App.Maui/_Profiling/aothelper.mibc`

- [ ] **Step 1: Regenerate the AOT sources**

Run: `head -5 update-aot-helpers.cmd`. If the script is a bash/cmd polyglot, run `./update-aot-helpers.cmd`. Otherwise run `dotnet run --project src/dotnet/App.AotHelper -- -g` followed by `dotnet run --project src/dotnet/App.AotHelper -- -m`.
Expected: `ApiAotSource.g.cs` now mentions `ArrivalKind`, `FunnelEvent`, `Usage_RecordOnboardingStep` and `Usage_RecordFunnelEvent`.

- [ ] **Step 2: Build the server and run the affected test projects**

Run:
```bash
dotnet build src/dotnet/App.Server/App.Server.csproj
dotnet test tests/Users.UnitTests/Users.UnitTests.csproj
dotnet test tests/Users.IntegrationTests/Users.IntegrationTests.csproj
dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj
dotnet test tests/Contacts.IntegrationTests/Contacts.IntegrationTests.csproj
```
Expected: the build succeeds and the tests pass. `AppLocalizationTest` must pass too; no user-visible strings were added. For any failure, check whether it also fails on `origin/dev` before touching it, and report pre-existing failures instead of fixing them.

- [ ] **Step 3: Manual pass on the local server**

Use `/debug-ui` for a guest session in host Chrome.
1. Get a `/join/<inviteId>` link from a signed-in user's chat.
2. Open it as a guest, sign up with a new test account, and go through onboarding, skipping at least one skippable step (e.g. Email).
3. Check the rows in the local Users DB:
   `SELECT kind, source_id, value, attributes FROM usage_events WHERE user_id = '<new id>' ORDER BY occurred_at;`
   Expect one kind-5 row `join:<inviteId>` with `ArrivalKind` in attributes, kind-6 rows including a `0` for the skipped step, and `Finished`.
4. Check that `c.Arrival` is gone.
5. Tap Copy, Share and QR on the invite banner. With the server run under `server-loop`, confirm the counter increments via the OTLP exporter's console/debug output, or by setting a breakpoint or temporary log in `FunnelMeters.Record` (remove it afterwards).
Record what was and wasn't verified for the final report.

- [ ] **Step 4: Commit**

```bash
git add -A src/dotnet
git commit -m "chore(aot): regenerate the AOT sources

Refs #4802

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 10: Team-only report in `../docs-internal`

**Files:**
- Create: `/home/undead/projects/docs-internal/docs/arrival-funnel.md` (a separate repo; commit there, never push)

**Interfaces:**
- Consumes: the table and column names as they are actually stored (verified in Step 1), `kind` values 0 (Speech), 5 (SignUp) and 6 (OnboardingStep), and the counter `usage_funnel_events_total`.

- [ ] **Step 1: Confirm the physical names locally**

Find the local Users DB connection string: `grep -rn "Users" src/dotnet/App.Server/appsettings*.json | grep -i "connection\|Db"`. Then check the actual table, column and jsonb shape:
```sql
\d usage_events
SELECT attributes FROM usage_events WHERE kind = 5 LIMIT 3;
```
Note whether the attributes JSON uses `ArrivalKind` or `arrivalKind`, and a number or a name. Adjust every query below to match before writing the doc.

- [ ] **Step 2: Read `../docs-internal/alloydb.connect.sh` and `../docs-internal/INTRO.md`** to learn how an agent connects to prod read-only. The doc's agent instructions must quote the real commands.

- [ ] **Step 3: Write the doc** with these sections and queries (adjust names per Step 1):

````markdown
# Arrival, onboarding and activation report

What this answers:
- activation rate by arrival path and week
- where onboarding stops
- funnel counters: link → sign-in → joined, and banner → share

Source: `usage_events` in the Users DB (per user), and `usage_funnel_events_total` in Managed Prometheus (counts).
Issue #4802.

## What is recorded
| kind | name | source_id | value |
|---|---|---|---|
| 0 | Speech | `<chatId>:0:<localId>` | ms |
| 5 | SignUp | `web`, `store`, `join:<inviteId>`, `user:<userId>`, `campaign:<id>` | 1 |
| 6 | OnboardingStep | step name (see list below), `Finished` | 1 completed / 0 skipped |

Step order: TranscriptionTutorial, PlacesTutorial, SummarizationTutorial, Phone, Email, Avatar, Permissions,
Languages, DataCollection, Passkey, Finished. Rows exist only from the release that shipped #4802.

Counter: `usage.funnel.events` (`usage_funnel_events_total` in PromQL), tags `event`, `app` and `arrival`.
The `arrival` tag is set only on SignUp.

## Activation by arrival path and week
"Activated" = within 7 days of sign-up, the user spoke in a chat where someone else also spoke in the same window.

```sql
WITH signups AS (
  SELECT user_id, occurred_at AS signed_up_at, split_part(source_id, ':', 1) AS arrival_kind,
         date_trunc('week', occurred_at) AS week
  FROM usage_events
  WHERE kind = 5 AND occurred_at >= :from AND occurred_at < :to
),
own_speech AS (
  SELECT DISTINCT s.user_id, split_part(e.source_id, ':0:', 1) AS chat_id, s.signed_up_at
  FROM signups s
  JOIN usage_events e ON e.user_id = s.user_id AND e.kind = 0
   AND e.occurred_at >= s.signed_up_at AND e.occurred_at < s.signed_up_at + interval '7 days'
),
activated AS (
  SELECT DISTINCT o.user_id
  FROM own_speech o
  JOIN usage_events e ON e.kind = 0 AND e.user_id <> o.user_id
   AND split_part(e.source_id, ':0:', 1) = o.chat_id
   AND e.occurred_at >= o.signed_up_at AND e.occurred_at < o.signed_up_at + interval '7 days'
)
SELECT s.week::date, s.arrival_kind, count(*) AS signups, count(a.user_id) AS activated,
       round(100.0 * count(a.user_id) / count(*), 1) AS rate_pct
FROM signups s LEFT JOIN activated a USING (user_id)
GROUP BY 1, 2 ORDER BY 1 DESC, signups DESC;
```
For a single arrival (e.g. one invite), filter `source_id = 'join:<id>'` in `signups`. A cohort is complete only 7 days after its last sign-up.

## Onboarding drop-off
```sql
WITH signups AS (
  SELECT user_id FROM usage_events WHERE kind = 5 AND occurred_at >= :from AND occurred_at < :to
),
steps AS (
  SELECT e.user_id, e.source_id AS step, e.value,
         array_position(ARRAY['TranscriptionTutorial','PlacesTutorial','SummarizationTutorial','Phone','Email',
           'Avatar','Permissions','Languages','DataCollection','Passkey','Finished'], e.source_id) AS idx
  FROM usage_events e JOIN signups USING (user_id) WHERE e.kind = 6
),
last_step AS (
  SELECT DISTINCT ON (user_id) user_id, step, idx FROM steps ORDER BY user_id, idx DESC
)
SELECT coalesce(l.step, '(no step)') AS last_step, count(*) AS users
FROM signups s LEFT JOIN last_step l USING (user_id)
GROUP BY l.step, l.idx ORDER BY l.idx NULLS FIRST;
```
Per step, completed vs. skipped:
```sql
SELECT source_id AS step, sum(value) AS completed, count(*) - sum(value) AS skipped
FROM usage_events WHERE kind = 6 AND occurred_at >= :from AND occurred_at < :to
GROUP BY 1 ORDER BY 1;
```
The drop-offs are the users whose last step isn't `Finished`. Steps completed before the modal opened (phone already verified, etc.) are skipped automatically and have no row.

## Funnel counters (PromQL)
```bash
TOKEN=$(gcloud auth print-access-token)
curl -s -H "Authorization: Bearer $TOKEN" \
  https://monitoring.googleapis.com/v1/projects/actual-chat-app-prod/location/global/prometheus/api/v1/query \
  --data-urlencode 'query=sum by (event) (increase(usage_funnel_events_total[7d]))'
```
- Link funnel: `JoinOpenedSignedOut` → `SignInRequestedFromLink` → `SignInCompletedFromLink` → `SignUp{arrival="Join"}` → `JoinUsed`.
- Banner funnel: `InviteBannerShown` → `InviteShare` + `InviteCopy` + `InviteQr` → next week's `SignUp` by `arrival`.
- Contacts: `ContactsAccessGranted` → `ContactsMatched`.
- Split by platform with `sum by (event, app)`.

`increase()` extrapolates across pod restarts. For per-day numbers, use `query_range` with `step=3600` over the raw counter, and sum the per-series deltas.
Shares are taps, not completed shares.

## Instructions for an agent
1. Read-only. Never write to the prod DB, and never paste per-user rows or ids into chat. Report aggregates only.
2. Pick the query that answers the question. Set `:from`/`:to` to UTC dates, e.g. last Monday–Monday.
3. SQL: connect with `<the real alloydb.connect.sh invocation from Step 2>` and run the query with psql variables (`\set from '2026-09-21'`).
4. Counters: run the PromQL `curl` above. The project is `actual-chat-app-prod` (dev: `actual-chat-app-dev`).
5. Report the window, the cohort size and the numbers. Flag cohorts younger than 7 days as incomplete.
````

- [ ] **Step 4: Verify every SQL query against the local DB**

Use the account from Task 9 Step 3; add a second account that speaks in the same chat to get a non-zero activation. Fix any query that errors or returns something implausible. Replace `:from`/`:to` with literals while testing.

- [ ] **Step 5: Commit in docs-internal**

```bash
cd /home/undead/projects/docs-internal
git add docs/arrival-funnel.md
git commit -m "docs: arrival, onboarding and activation report (#4802)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```
Do not push.
