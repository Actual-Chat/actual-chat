# Flake patterns

The mechanisms behind the test failures CI has actually shown, and what to do about each.
It serves two readers: a person looking at a red test, and the CI watchdog, which matches
failures against this page and writes a triage card for each one it reports (format at the
end).

[Waiting in tests](./waiting.md) covers how to write a wait. This page starts from the
other end: a failure you already have.

[[toc]]

## Reading a failure

Before naming a mechanism, collect what the run already recorded:

- **The test output, not just the assertion.** xUnit prints `Standard Output Messages`
  for a failed test, with a timestamp on every line. The order of events there settles
  most arguments. In #5540 the render wait succeeds at `08.110` and the lookup that
  failed ran in the same millisecond.
- **bUnit's own log** for UI tests: checks, renders and the wait's end, timestamped. See
  [Waiting for a render](./waiting.md#waiting-for-a-render).
- **The host heartbeat** (`alive: tick …`) for anything slow: was the machine short of CPU
  or I/O at that moment.
- **The code at the failing SHA**, not at `dev` — the test may have changed since.
- **What landed after it.** `git log <sha>..origin/dev -- <test file> <code under test>`,
  and the same against the release branch. A fix may already be in.

A log that cannot tell two mechanisms apart is a finding in itself: the action is to make
the next failure tell — `instrument` in [Actions](#actions).

## Patterns

Each pattern says what the failure looks like, why it happens, how to confirm it, and the
shape of the fix.

### Asserting without waiting

**Looks like:** a value one step behind — a count short by one, a state not yet updated —
on a line right after a write, a send or an enqueue.

**Why:** the write returned before its effect was visible; locally the effect wins the race,
under CI load it loses.

**Fix:** wait for the effect. Details and examples in
[The most common flake: asserting without waiting](./waiting.md#the-most-common-flake-asserting-without-waiting).

### Reading after the wait, outside it

**Looks like:** the wait passes, and the very next line fails on something the wait did not
check. The test output shows the two within a millisecond.

**Why:** a wait proves only what its assertion checked, at the moment it checked it. Any
other value read after it is read once, with no retry, and may not have caught up yet — or
may already have moved on.

**Confirm:** the failing line is outside the wait's lambda, and it reads a different value
than the wait checked.

**Fix:** move the read into the same wait, so the whole condition has to hold at once.
Example: #5540, fixed in #5541 — the banner's icon and text were checked after a wait on its
visibility.

### Checking a component parameter instead of its markup

**Looks like:** `FindComponent<X>().Instance.SomeParameter` has the expected value, yet a
`Find(…)` on the markup fails with `ElementNotFoundException`.

**Why:** Blazor assigns parameters in `SetParametersAsync`, before the component renders. A
parameter that says "visible" proves the parent asked for it, not that the markup shows it.
In #5540 the previous render was a hidden
[Banner](https://github.com/Actual-Chat/actual-chat/blob/main/src/dotnet/UI.Blazor/Components/Banner/Banner.razor),
which outputs no markup at all.

**Confirm:** the parameter assertion passed and the markup lookup failed right after it.

**Fix:** assert the markup, or assert both inside one `TestWait.WhenRendered` — bUnit re-runs
the assertion after every render, on the renderer's dispatcher, so it passes only on a render
where the two agree.

### Measuring a side channel instead of counting the result

**Looks like:** an off-by-some on a derived number — an ID range, a sequence position, a
total computed from IDs — while a direct count in the same test passes.

**Why:** the side channel moves for reasons the test does not control. In #5494 a send took
its LID from the Redis sequence before the cap check rejected it, and a rejected transaction
does not return the LID: two accepted sends could hold LIDs 1 and 3, and the range spanned
the gap.

**Confirm:** find what else advances the channel — a rejected or rolled-back operation, a
retry, a concurrent writer.

**Fix:** count the thing you mean (accepted entries, not their ID span). Fixed in #5496.

### A race in the product, caught by a test

**Looks like:** a result that is wrong in a way no wait would fix — work done twice,
content fetched again, a duplicate row. The test is right.

**Why:** check-then-act across two places the code does not lock together. In #5493 a lookup
checked the disk first and the active fills second, and a fill that finished between the two
checks was missed by both, so the same URL was downloaded twice.

**Confirm:** the code under test reads two pieces of state without a common lock, and one
can change between the reads. A stress run that widens the gap usually reproduces it.

**Fix:** in the product, not the test — `product-bug` in [Actions](#actions). Fixed in #5495
by looking up active fills before the disk.

### bUnit's own wait

**Looks like:** a UI test times out after exactly 1 s, also on CI.

**Why:** bUnit's `WaitForAssertion` has a fixed 1 s budget that does not scale on a build
agent.

**Fix:** `TestWait.WhenRendered` — see [Waiting for a render](./waiting.md#waiting-for-a-render).

### A ceiling below the budget

**Looks like:** `Test execution timed out` with nothing else, or a timeout that started the
day budgets were scaled.

**Why:** `[Fact(Timeout = N)]` is a constant and does not scale, while the waits under it do.

**Fix:** see [Guards fire in order, and the ceiling does not scale](./waiting.md#guards-fire-in-order-and-the-ceiling-does-not-scale).
Never by raising a wait budget to make a red test green.

### A first host pays the cold start

**Looks like:** a `TaskCanceledException` from the test's own `CancellationTokenSource`, in
a test that starts its own app hosts inside that deadline. The failing line is late in the
test, often where it only just began a step that takes a second.

**Why:** the first app host built in a test process loads and JIT-compiles the whole server,
and on CI the other collections are building their first hosts at the same moment, on a
runner already short of CPU. In #5565 the first host of `ShardedComputeServiceBaseTest` took
22 s and 17.5 s of a 30 s deadline, most of it inside `AppHost.Start()` before
`App.Server is starting`, while its second host started in 2.4 s under a heavier load. The
step the test exists for got under a second.

**Confirm:** in the test output, the time from `-> NewAppHost` to `<- NewAppHost` of the
first host against the second, and the heartbeat's `run` and `loadavg` across both. A first
host many times slower than the second, with the load no lower, is this pattern; both slow
is a starved runner.

**Fix:** start the deadline once the hosts are up (`cts.CancelAfter(…)` after the last
`NewAppHost`), so it covers only what the test checks. Not by raising the deadline: the
cold start grows with the server and with the runner's load. Fixed this way for #5565.

## Open mechanisms

Failures whose mechanism is not established yet. Each lists what the evidence shows so far,
so the next failure is read against it rather than from scratch.

### Synced states that never converge (#5497)

`ComputedKvasTest.SyncedStateTest1/2`: two `SyncedState`s on one key are set back to back and
each keeps its own value. The journal added in #5498 shows the invalidation is **not** lost —
both `ReadState`s are invalidated after the write — but neither re-reads the key afterwards.
#5523 adds the update-cycle steps to the journal (`delay` / `delayed` / `updating`), so the
next failure names the step of `ComputedState.UpdateCycle` that stalls.

## Actions

Every triage ends in exactly one of these:

| Action | When | What follows |
|---|---|---|
| **Fix the test** (`fix-test`) | The test is wrong; the product behaves as designed | A change under `tests/` — only the kinds listed below |
| **Add instrumentation** (`instrument`) | The log cannot tell the candidate mechanisms apart | A journal or a dump the next failure prints — #5498 and #5523 are examples |
| **Product bug** (`product-bug`) | The test is right and the product misbehaves | An issue for the owner of the code under test, describing the user-visible symptom |
| **Wait** (`wait`) | A fix landed after the failure | Re-check after a few days of runs |
| **Nothing** (`none`) | Infrastructure, or a one-off with nothing to act on | Say why |

### What a test fix may and may not do

A test fix may only make the check **stricter or equivalent**:

- move a read into the wait that guards it;
- wait for the markup or the final event instead of a parameter or an intermediate flag;
- count the result instead of measuring a side channel;
- add a journal or a dump printed on failure.

It may **not** hide the failure:

- weaken or delete an assertion;
- raise a wait budget or a timeout;
- add `FlakyFact`, `Skip` or retries;
- change product code — that is a product bug, not a test fix.

A change that makes a red test green by asking less of the product turns a signal into a
blind spot. When in doubt between "the test is wrong" and "the product is wrong", the action
is instrumentation.

## Triage card

The CI watchdog posts one per failure it triages, as a comment on the issue the failure
concerns. A person writing up a failure by hand can use the same shape.

````markdown
<!-- watchdog-triage -->
**Triage** — `<FullyQualifiedTestName>` · [run <id>](<url>) · `<branch>` @ `<sha>`
Signature: `<test name> | <first line of the error, at most 80 chars>`

**What failed.** <the assertion or exception, and the line it came from>

**Evidence.** <the 3–8 log lines that matter, with timestamps>

**Mechanism (confidence: high | medium | low).** <how it fails, in terms of the code; the
pattern from this page, if one fits>
What would confirm it: <the one observation that would settle it, or "confirmed by the log above">

**Already fixed?** <"no: nothing after <sha> touches <files>" | the commit or PR that does>

**Proposed action:** fix-test | instrument | product-bug | wait | none — <one line why>

<details><summary>Draft change (untested)</summary>

```diff
<unified diff against origin/dev, files under tests/ only>
```
</details>
````

- `confidence: high` only when the log or the code settles the mechanism, not when it is
  merely plausible. At `low`, the action is `instrument` or `none`, never `fix-test`.
- One card per signature: a second failure with the same signature links the first card
  rather than repeating it.
- The draft is a proposal. Nothing runs it before a person picks it up.
- Once a card's outcome is known, a person reacts to the card: 👍 the proposed change (or an
  equivalent) fixed it, 😕 the mechanism was right but the fix was different, 👎 the cause was
  something else. No reaction means no outcome yet. The watchdog doesn't read them: they are
  collected later to measure how often the cards are right (#5546).
