# Waiting in tests

Almost every integration test here waits for something: an invalidation to
propagate, a queued event to be handled, a shard to change hands. How that wait
is written decides whether the test is a test or a flake — the CI watchdog's
findings are dominated by two mistakes, asserting without waiting at all and
waiting on a budget that only fits an idle machine.

Naming, FluentAssertions and the AAA layout are in
[CODING_STYLE.md → Test Conventions](../CODING_STYLE.md#test-conventions); how
to run the suites is in [Overview](./overview.md). This page is only about the
wait itself.

## One entry point: `TestWait`

`ActualChat.Testing.TestWait` (`tests/Testing/TestWait.cs`) is the only way tests
wait. Do not call `ComputedTest.When` or `TestExt.When` directly — every budget
goes through `TestWait` so the build-agent scale and the measurement are applied
in one place.

| You need | Use |
|---|---|
| A computed value to reach a state | `TestWait.When(async ct => …)` |
| The same, with the value returned | `var x = await TestWait.When(async ct => { …; return v; })` |
| A specific `IServiceProvider` | `TestWait.When(services, async ct => …)` |
| Something not driven by invalidation | `TestWait.WhenPolled(async () => …)` |
| The same, with a value | `TestWait.WhenPolled<T>(async () => …)` |

## `When` or `WhenPolled`

**`When` is reactive.** It runs the assertion inside a `ComputedSource`, so it
re-runs when anything the assertion read is invalidated, and returns the moment
it passes. No polling interval, no wasted round trips. This is the default —
reach for it first.

**`WhenPolled` re-runs on a timer** (50 ms by default). It exists for the cases
`When` cannot see:

- **The value ends up unchanged.** A compute method with
  `[ComputeMethod(ConsolidationDelay = 0)]` recomputes on invalidation and drops
  the invalidation when the output is identical, so there is nothing for `When`
  to wake up on. `AppUpdates.GetLatestUpdateInfo` is the live example — see
  [App updates](../app-updates.md).
- **You are waiting for a side effect**, not a value: a probe's call count, a
  row written by a background loop, a lock released.
- **The assertion itself drives the state** — it calls `Invalidate()` and then
  reads, so each attempt has to be a fresh attempt.

Say which one it is when it isn't obvious; a future reader will otherwise
"simplify" the poller back into `When` and hand you an intermittent failure.

The value-returning `WhenPolled<T>` needs its type argument spelled out. Without
it the non-generic overload wins the tie-break and the lambda's result has
nowhere to go (CS8030/CS8031).

## Budgets

`TestWait.DefaultTimeout` is 10 s, and **on a build agent every budget is
multiplied by `TimeSpanExt.CiScale` (3)**. An agent runs the whole suite in
parallel next to PostgreSQL, Redis, NATS and OpenSearch, so the same wait takes
a multiple of its local time there.

```csharp
await TestWait.When(async ct => …);                     // 10s local, 30s on CI
await TestWait.When(async ct => …, TimeSpan.FromSeconds(20));  // 20s local, 60s on CI
await TestWait.When(async ct => …, LockExpiration, isExactTimeout: true);  // 15s everywhere
```

Pass `isExactTimeout: true` only when the number is tied to something that does
**not** scale — a lock expiration, a configured period the test itself set.

A generous budget costs nothing while the test passes: a successful wait ends on
the event it waits for, not on its budget. It costs only on failure, and a
failed wait is a red test either way.

**Do not raise `[Fact(Timeout = N)]` reflexively.** Those numbers were mostly
chosen with CI in mind already. The attribute takes a constant and cannot scale,
so it is the ceiling every internal budget lives under: raise an internal budget
past it and the failure arrives as a useless "Test execution timed out" instead
of the assertion that would have named the problem. #4654 was exactly that —
`LiveAudioBackendShardMigrationTest` had `Timeout = 30_000` around a single 30 s
internal wait.

## The most common flake: asserting without waiting

A test that writes, then reads, then asserts is not waiting for anything — it is
betting on the machine. The bet holds locally and loses under CI load.

```csharp
// wrong - Enqueue returns before the handler ran
await sender.Enqueue(command);
(await testService.GetProcessedEventCount()).Should().Be(2);

// right
await TestWait.When(async ct =>
    (await testService.GetProcessedEventCount(ct)).Should().Be(2));
```

`services.Queues().WhenProcessing()` covers the **default** queue processors, not
a custom queue's own reader — a test that enqueues into its own queue still has
to wait for the effect it cares about.

Whatever the wait, assert the thing you mean. `Should().NotBeNull()` passes on a
stale value as happily as on the new one; assert the value.

## Local wrappers

When a whole suite shares a budget or a poll interval, wrap it once rather than
repeating the argument at every call. Forward `[CallerFilePath]` and
`[CallerLineNumber]` so the wait report still points at the real call site
instead of collapsing the suite into one line:

```csharp
private static Task When(
    Func<CancellationToken, Task> assertion,
    [CallerFilePath] string callerFilePath = "",
    [CallerLineNumber] int callerLine = 0)
    => TestWait.When(assertion, TestTimeout, callerFilePath: callerFilePath, callerLine: callerLine);
```

## The wait report

Every budget in the tests was guessed; nothing ever measured how long a wait
really takes under load. `TestWait` now writes one line per wait:

```
332/3000ms ok  CancellingDebouncerTest.cs:51
9765/30000ms ok RoutingStressTest.cs:172
```

Format: elapsed/budget, `ok` or `fail`, call site. It is on when
`TestRunnerInfo.IsBuildAgent()` is true or `ActualChat_TestWaitReport` is set,
and writes to `artifacts/tests/bin/<Project>/debug/test-waits-<time>-<pid>.log` —
a file rather than the console, because xUnit prints a test's captured output
only when it fails, which would hide exactly the passing waits worth measuring.

CI uploads them as `test-waits-<report-name>` artifacts (7 days):

```bash
gh run download <runId> -p 'test-waits-*' -D tmp/waits
```

The second line above is the point of the exercise: that wait used 9765 ms of a
10 s budget before the scale existed, which is why #4700 failed on CI and never
locally. What to do with the numbers — a smarter scale, or just honest constants
— is decided from them in #4709, not by guessing again.
