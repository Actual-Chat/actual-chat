# Usage and Funnel Metrics

What Voxt records about how people arrive, get through onboarding and start talking, and what
to keep in mind when you add an onboarding step, a new way in, or a new funnel step. The
numbers themselves are read with the team-only report in the `docs-internal` repo
(`docs/arrival-funnel.md`: SQL, PromQL and instructions for an agent).

## What is recorded

**Per user**, in the usage log (`UsageEvents`, written through `UsageBackend_Record`, deduplicated
on `(user, Kind, SourceId)`):

| Kind | Written when | `SourceId` | `Value` |
|---|---|---|---|
| `SignUp` (5) | the account is created (`AccountsBackend.OnSignIn`) | the arrival: `web`, `store`, `join:<inviteId>`, `user:<userId>`, `campaign:<id>` | 1 |
| `OnboardingStep` (6) | the user completes or skips a step (`Usage_RecordOnboardingStep`) | the step name, or `Finished` | 1 completed, 0 skipped |

Activation needs nothing extra: `Speech` rows carry `<chatId>:0:<localId>`, so "spoke in a chat where
someone else spoke too" is a query over existing rows. Neither kind creates a `UsageDay` row
(`UsageEventKindExt.IsDayRollup`), because a day row counts as an active day for the review prompt.

**Counts**, in the OTLP counter `usage.funnel.events` (`FunnelMeters`, exported as
`usage_funnel_events_total`), tagged `event`, `app` and, on `SignUp` only, `arrival`. It covers
everything that happens before an account exists or doesn't belong to one user: links opened,
sign-in from a link, joins, invite-banner impressions and taps, contacts access.

## Adding or changing an onboarding step

1. Add the step component to `OnboardingModal.razor`, in the position it should appear.
2. Add its name to `OnboardingSteps.All`, in the same position; `Finished` stays last. The name is
   the type name without `Step` (`OnboardingSteps.GetName`). The server rejects any other name, so a
   step missing from the list is silently never counted. `OnboardingStepsTest` fails the build for
   that. A step component the modal doesn't render goes into that test's `disabledStepTypes`.
3. Add the name to the step-order array in the report's drop-off query.
4. Advance the stepper only through `Stepper.TryMoveForward` (reported as completed) or
   `Stepper.Skip` (reported as skipped). The modal's Next/Skip buttons and `PhoneStep` already do.
   A button that means "next" must not call `Skip`: the tutorial slides once did, and every
   tutorial read as skipped.

Keep in mind:

- **Renaming a step component renames its data.** Rows already recorded keep the old name, so the
  report splits one step in two. Rename only if that is acceptable, and fix the report's array.
- **Steps done before the modal opened leave no row.** `SkipCompletedSteps` moves past them without
  a report, e.g. Phone for someone who signed up with a phone number. A missing row is not a
  drop-off.
- **The first report of a step wins.** Going back and redoing a step doesn't change its row.

## Adding a way in (an entry point)

The arrival travels from the client to sign-up in the `c.Arrival` session temporal:

- **A page that is a way in** calls `AccountUI.SetArrival(arrival)` while the visitor is a guest,
  as `ChatInvitePage` (`join:`) and `UserPage` (`user:`) do. The last link a guest opened wins.
- **The landing URL** (`utm_campaign` or `c`) and the Android Play install referrer are captured
  by `AccountUI` on its own. They only fill an empty arrival, so a link page always wins.
- `AccountUI` keeps the arrival in local storage until an account exists and re-sends it at start-up,
  every 5 minutes and when the sign-in dialog opens: the server forgets a session temporal 10
  minutes after its last write. The server consumes it on any sign-in.
- **A new kind of way in** needs a new `ArrivalKind`: append the value (it is stored as a number),
  and teach `ArrivalInfo` its prefix in both `FormatKind` and `TryParseKind`. Ids are validated
  there (1–64 chars of `[A-Za-z0-9_.@-]`); anything else falls back to `web`/`store`.
- **If the new page asks the visitor to sign in**, do it with `AccountUI.RequestSignInFromHomePage`
  and extend its "from a link" check, which today only matches `/join/` URLs. That is what marks
  the sign-in so the server counts `SignInCompletedFromLink`, even across a full-page redirect.

## Adding a funnel step (a counter)

1. Append a value to `FunnelEvent`. It is serialized as a number in commands, so never renumber.
2. Count it where it happens:
   - **The server can see it** (a command succeeded, an account was created): send it as an
     operation event, so only a committed operation counts and a retry doesn't double-count:
     `context.Operation.AddEvent(new UsageBackend_CountFunnelEvent(userId, FunnelEvent.X, session));`
     Then add it to the server-only list in `FunnelEventExt.IsClientReportable`, so a client can't
     inflate it.
   - **Only the client can see it** (a page opened, a tap, something shown):
     `Hub.RecordFunnelEvent(FunnelEvent.X)`. It sends `Usage_RecordFunnelEvent` and never throws.
     Guard against re-renders: count once per component instance or per app run, as
     `ChatInvitePage` and `ChatListUI.ReportInviteBannerShown` do.
3. Add it to the funnel table in the report.

Keep in mind:

- **Measurement must never fail or block a user flow.** Never await a funnel call on a render
  path. Server-side measurement code catches everything except its own cancellation.
- **Tags must stay bounded.** Never put an id, a URL or free text into a tag; per-user detail
  belongs in the usage log.
- **`AppUIInstruments` counters are not an option for this.** Only the server exports metrics;
  a counter created in WASM or MAUI is never seen.

## Checking a change

- **Tests.** `tests/Users.IntegrationTests/UsageTest.cs` shows both patterns: reading usage rows
  straight from the DB with `TestWait.WhenPolled`, and catching counter increments with a
  `MeterListener` (`FunnelEventListener`).
- **Locally, end to end**, with the server running:
  - Watch the counters live:
    `dotnet-counters collect -p <server pid> --counters ActualChat.Core.Server --refresh-interval 1 --format csv -o tmp/counters.csv`
    (install once with `dotnet tool install -g dotnet-counters`). The file lags a few seconds.
  - Read the usage rows in the `ac_dev_users` database:
    `SELECT kind, source_id, value, attributes FROM usage_events WHERE user_id = '<id>' ORDER BY occurred_at;`
  - Check the session temporals in Redis: `HGETALL` on the session's `Tmp` hash shows
    `c.Arrival` and `c.SignInFromLink`.
  - Run a new report query against a `TEMP` table created with
    `CREATE TEMP TABLE usage_events (LIKE public.usage_events)`: it shadows the real table for the
    session, so you can check it on a known scenario.
- **Driving the UI**: `/debug-ui` covers two Chrome profiles; `debugUI.signIn` with a new
  `test-*@actual.chat` email creates a new account, and `{ skipOnboarding: false }` keeps the
  onboarding modal.
