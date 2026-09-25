# Arrival links, onboarding drop-off and 7-day activation — design

Issue: [#4802](https://github.com/Actual-Chat/actual-chat/issues/4802).
Branch: `feat/4802-measure-arrival-links-onboarding`. One PR.

## Goal

Answer three questions from data we already export or store:

1. What share of last week's sign-ups that arrived through `/join/` links had a two-way voice exchange
   within 7 days?
2. At which onboarding step do people drop off?
3. How many invite-banner shares turn into sign-ups?

No new analytics service and no new tables. Per-user facts go into the existing usage log
(`UsageEvents`). Pre-account and anonymous facts go into OTLP counters next to `UsageMeters`. The
report is a team-only document in `../docs-internal`.

## Decisions made while brainstorming

- Everything ships in one PR: the usage events, the funnel counters and the report.
- Campaigns come from `utm_campaign` (or `?c=`) on the landing URL, **and** from the Play Install
  Referrer on Android's first launch. iOS installs are always recorded as `store`.
- The arrival path reaches the server through a **client-writable session temporal**. We rejected
  two alternatives. A post-sign-up client command can be lost or replayed. Carrying the path through
  OAuth state or a return URL would be provider-specific.
- The report goes to `../docs-internal/docs/arrival-funnel.md`, with SQL, PromQL and instructions
  for an agent.

## A. Arrival capture → `SignUp` usage event

### Arrival string

The arrival string is a short value written to session temporal `c.Arrival`. The key goes through
`Constants.SessionTemporals.ToClientKey`, so clients can write it.

| Arrival | Produced when |
|---|---|
| `join:<inviteId>` | A `/join/<inviteId>` URL was opened. |
| `user:<userId>` | A `/u/<route>` URL was opened. `UserPage` already resolves the route (and `/u/@alias`) to a user id, so the client writes the id. |
| `campaign:<id>` | The landing URL carried `utm_campaign=<id>` or `c=<id>`, or the Android Play Install Referrer did. |
| *(absent)* | The server falls back to `web` or `store`, chosen from the session's app kind. |

Rules:

- **The client writes only while the session is a guest.** Once signed in, it stops writing.
- **Last link-touch wins.** Each new `/join/`, `/u/` or campaign URL opened as a guest overwrites
  the value. The link that led to the sign-in request is the one we want.
- **Parsing and validation are shared.** One `ArrivalInfo` type in `ActualChat.Api` (next to
  `UsageEvent`) owns `Parse`/`Format`. The id part is capped at 64 characters, restricted to
  `[A-Za-z0-9_\-.@]`, and ignored otherwise. The client uses it to format the value and the server
  uses it to parse the value back.
- **Client placement.** `AccountUI.Arrival.cs` is a small partial of `AccountUI` (CODING_STYLE #14:
  extend an existing UI service). It exposes `SetArrival(ArrivalInfo)`, which writes only while
  the account is a guest. `ChatInvitePage` calls it with `join:<inviteId>`, and `UserPage` with
  `user:<userId>` once the route is resolved. Deep links on MAUI arrive as Blazor navigations to
  these pages, so they are covered too. A campaign comes from the query of the first URL `History`
  saw (`ArrivalInfo.FromLandingQuery`) and is written from `AccountUI`'s worker at start-up.
- **Android Play Install Referrer.** This adds the `Xamarin.Google.Android.InstallReferrer`
  package. `IInstallReferrer` (in `UI.Blazor/Services`, next to `IAppReviewer`) has
  `Task<string?> GetQuery(CancellationToken)` and an Android-only implementation registered in
  `MauiProgram.Android.cs`. On start-up, while the account is a guest and `c.Arrival` is empty,
  `AccountUI` reads the install referrer. If its query carries `utm_campaign`, `AccountUI` writes
  `campaign:<id>`. Play keeps returning the same referrer, so no "already read" flag is needed.
  Failures are logged and ignored.

### Server

In `AccountsBackend.OnSignIn`, when `isNew`:

1. Read `c.Arrival` from `ISessionTemporalsBackend`, then parse it with `ArrivalInfo`.
2. If there is no arrival, fall back to `store` when `AppKindExt.TryParseUserAgent(SessionInfo.Description)`
   says MAUI, and to `web` otherwise.
3. Write `UsageEvent(Kind: SignUp, OccurredAt: now, SourceId: <arrival>, Value: 1,
   Attributes: { ArrivalKind = … })` through `UsageBackend_Record`. The write goes through
   `context.Operation.AddEvent` so it runs after the account commit.
4. Clear `c.Arrival`. Bot accounts get no `SignUp`.

Model changes (no migration: `kind` is an integer and `attributes` is jsonb):

- `UsageEventKind.SignUp = 5`, `UsageEventKind.OnboardingStep = 6`.
- A new `enum ArrivalKind { Web = 0, Store = 1, Join = 2, User = 3, Campaign = 4 }` in `ActualChat.Api`.
- `UsageEventAttributes`: add `[DataMember, Key(4)] public ArrivalKind? ArrivalKind { get; init; }`.
- The new kinds get **no day row**. In `UsageBackend.OnRecord` and `OnRebuildDays`, any day row counts as an
  active day for the review prompt, so both handlers skip day handling for
  `!kind.IsDayRollup()` (`UsageEventKindExt`). `UsageDay` is unchanged.
- The builders are `UsageEventSource.SignUp(ArrivalInfo, Moment)` and
  `UsageEventSource.OnboardingStep(string step, bool isCompleted, Moment)`.

The inviter is not stored. For `join:` it is recoverable from `Invite.CreatedBy`. For `user:` it
is the id itself.

## B. `OnboardingStep` usage events

Skip and complete write the same settings flag, and Permissions is stored only locally. So the
server can't infer steps, and the client must report them.

- The API command is `Usage_RecordOnboardingStep(string Step, bool IsCompleted)` on `IUsage`,
  modelled on `Usage_RecordReviewPrompt`. It requires an active (non-guest) account, validates
  `Step` against `OnboardingSteps.All`, and records through `UsageBackend_Record`.
- **Source id.** The source id is the step name. Dedup on `(Kind, SourceId)` keeps one row per user
  and step, and the first report wins. `Value` is 1 for completed and 0 for skipped.
- **Step names.** `OnboardingSteps` is a static list in `ActualChat.Api`. It holds the names in
  modal order: `TranscriptionTutorial, PlacesTutorial, SummarizationTutorial, Phone, Email, Avatar,
  Permissions, Languages, DataCollection, Passkey, Finished`. `Finished` is written when the stepper
  completes (`OnboardingModal.OnCurrentStepChanged(isCompleted: true)`), because the last visible
  step varies: Passkey is conditional.
- **Client hook.** `Stepper` gets a new `[Parameter] EventCallback<StepFinishedArgs> StepFinished`,
  raised from `Stepper.TryMoveForward` (after `TryComplete` succeeds) and `Stepper.Skip`. Both
  the footer buttons and `PhoneStep` go through these methods. Steps the user had already done are
  auto-skipped by `Move` and never raise it. The step name is
  `OnboardingSteps.GetName(step.GetType())`, which is the type name minus `Step`. `OnboardingModal` subscribes and sends the command through `UICommander` without
  awaiting it, so errors never block onboarding.
- **Drop-off.** A user dropped off when they have a `SignUp` row and no `Finished` row. Their
  last step is the step with the highest `OnboardingSteps` index among their rows.

## C. Funnel counters (OTLP)

A new `FunnelMeters.Events` counter lives next to `UsageMeters` in `Users.Service/Usage`. It is
named `usage.funnel.events` and has tags `event` and `app`, where `app` comes from
`AppKindExt.TryParseUserAgent`.

`FunnelEvent` enum (in `ActualChat.Api`):

| Event | Counted where |
|---|---|
| `JoinOpenedSignedOut`, `JoinOpenedSignedIn` | Client, `ChatInvitePage` |
| `JoinUsed` | Server, `InvitesBackend.OnUse` success |
| `UserLinkOpenedSignedOut`, `UserLinkOpenedSignedIn` | Client, `UserPage` |
| `SignInRequestedFromLink` | Client, `AccountUI.RequestSignInFromHomePage` when a redirect URL is a `/join/` or `/u/` URL |
| `SignInCompletedFromLink` | Client, `SignInRequest` completes signed in with that redirect URL |
| `SignUp` | Server, `OnSignIn` when `isNew`, with an extra tag `arrival` = `ArrivalKind` |
| `InviteBannerShown` | Client, `InviteFriendsBanner.OnInitialized`, deduplicated by a flag on `ChatListUI` (once per app run) |
| `InviteShare`, `InviteCopy`, `InviteQr` | Client, `ShareActions` (see below) |
| `ContactsAccessGranted` | Client, `PermissionHandler.CheckOrRequest` when a request it made ends granted and the handler is a `ContactsPermissionHandler` (via a protected virtual `OnRequestGranted`). This covers the #4801 banner once it uses the handler. |
| `ContactsMatched` | Server, `ContactLinker` where it creates a contact (`!contact.IsRegular` branch) |

The client reports events through one command, `Usage_RecordFunnelEvent(FunnelEvent Event)`, on
`IUsage`:

- **Guests may call it.** The worst abuse is an inflated counter.
- **Validation.** The server accepts only events marked client-reportable. `JoinUsed`, `SignUp`
  and `ContactsMatched` are server-only and rejected from the client.
- **Fire-and-forget.** The client sends it through a `RecordFunnelEvent(FunnelEvent)` extension
  method on the UI hub (a static `UsageUIExt`, no new service; there is no `UsageUI` today) and
  never awaits it on a render path. Errors are logged at debug level.

**ShareActions taps.** `ShareActions` gets `[Parameter] Action<ShareActionKind>? Tapped`, where
`ShareActionKind` is `Share, Copy, Qr`. The share button and the copy trigger are wrapped in
`<span class="contents" @onclick=…>`. The JS handlers in `copy-trigger.ts` and `share.ts` never
stop propagation, so Blazor sees the bubbling click without any JS change, and
`display: contents` keeps the layout. QR calls `Tapped` from `OnShowQrClick`. Only
`InviteFriendsBanner` passes `Tapped`, so other share sites stay uncounted. A tap is counted,
not a completed share.

**Why a client event is needed at all.** The exporter runs only on the server. The
`AppUIInstruments` counters in WASM and MAUI are never exported, so anything the client alone sees
must go through `Usage_RecordFunnelEvent`.

## D. Report (team-only)

The report lives at `../docs-internal/docs/arrival-funnel.md` (repo `docs-internal`, committed
separately) and has these sections:

1. **What is recorded.** Event kinds, the `SourceId` formats, and the counter names and tags.
2. **Activation by arrival path and week (SQL).** The cohort is the `SignUp` rows in the week.
   An activated user has a `Speech` row within 7 days of `SignUp` in a chat, taken from the
   `SourceId` prefix before `:0:`, where another user also has a `Speech` row within the same
   window. Output: week, arrival kind, sign-ups, activated, rate.
3. **Onboarding drop-off per step (SQL).** For each step, the number of users whose last step it
   was, plus how many completed and how many skipped it.
4. **Funnel counters (PromQL).** `usage_funnel_events_total` by `event`/`app`/`arrival` over the
   Monitoring API, following the `usage_review_prompt_outcomes_total` recipe. It also notes that
   `increase()` extrapolates across pod restarts and that `query_range` should be used for
   per-day counts.
5. **Agent instructions.** Connect to prod with `alloydb.connect.sh` (read-only use only). Pick the
   query that answers the question, substitute the date window, and run it. For counters, fetch
   a token with `gcloud auth print-access-token` and call the PromQL endpoint. Report the numbers
   together with the window and cohort size. It also lists what must never be done: no writes and
   no per-user data in chat output beyond counts.

Every query is checked against local dev data before the doc is committed.

## Testing

- **Unit tests** (`tests/Users.UnitTests`) cover:
  - `ArrivalInfo.Parse`/`Format`: every kind, garbage, over-length ids, invalid characters.
  - The `UsageEventSource.SignUp`/`OnboardingStep` builders.
  - The `FunnelEvent` client-reportable filter.
- **Integration tests** (`tests/Users.IntegrationTests/UsageTest.cs`):
  - A new account with `c.Arrival = join:<id>` gets one `SignUp` row with `ArrivalKind.Join`.
  - A new account with no arrival falls back to `web`.
  - An existing account signing in writes no `SignUp`.
  - `c.Arrival` is cleared after sign-up.
  - `Usage_RecordOnboardingStep` dedups per step, rejects unknown steps, and rejects guests.
  - `Usage_RecordFunnelEvent` rejects server-only events.
- **Waits** follow `docs/testing/waiting.md` and use only `TestWait`.
- **Manual pass** on the local server: open `/join/<id>` as a guest, sign up, go through onboarding
  skipping one step, then check the `UsageEvents` rows and the counter values.

## Out of scope

- Fixing the `/u/` guest flow (#4799). We only count it as it behaves today.
- The contacts-access banner itself (#4801). We count its grant if it exists when this lands,
  otherwise the `PermissionsUI` grant.
- iOS campaign attribution. There is no install-referrer equivalent.
- Rolling the new kinds up into `UsageDay`.
