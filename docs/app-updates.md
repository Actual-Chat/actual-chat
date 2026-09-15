# App updates — the "Update Voxt" banner

The "Install Voxt" banner becomes an **Update Voxt** banner when a newer build is
actually published in the user's store. In that mode it shows on every host
including MAUI mobile, ignores the dismissal flag, has no close button, and its
tap opens the store page — or, on the web, asks to reload.

The hard part is not the banner. It's knowing, per app kind, that the store
*serves* the new build: the server deploys hours to days before the stores do, so
"server version > client version" would send users to a store page with no update
on it.

## The contract

`IAppUpdates.GetLatestUpdateInfo(appKind)` (`Api.Contracts/Users/`) returns the
newest build known to be published for that kind, or `null` for *unknown* — which
is also the answer on every non-production instance, for `AppKind.Unknown`, during
the web grace period, and until the first probe lands.

`AppUpdateInfo.VersionString` is the build version a client compares itself
against, and `Version` is that parsed. Everything crossing the API is normalized
to `X.Y.Z` — `ParseBuildVersion` accepts `vX.Y.Z`, `X.Y.Z+sha` and `X.Y.Z.0`, and
only one of those forms should ever reach a client. `Version` is computed on
read rather than cached in a field, because the record is the output of a
consolidating compute method and a record's `Equals` compares every instance
field; `AppUpdateInfoTest` pins that.

There is no region dimension. Both stores publish to every storefront at once —
a staged rollout is a percentage of users, not a set of countries — Voxt is
listed in every storefront we sampled, and a device region read from the SIM or
the locale is only a rough proxy for the *account's* storefront anyway. So the
probes ask the US storefront and the answer stands for everyone. Instead of
splitting by region we **wait**: a detected release is announced only once
`AnnounceDelay` (1 hour) has passed since it was detected, which absorbs any
propagation lag, a rollout that is still ramping, and the gap between an iOS
release and the Mac build that shares its App ID.

Both sides parse versions through the one helper, `VersionExt.TryParseBuildVersion`,
which strips the nbgv `-alpha`/`+sha` tail and normalizes to three components.

## Detection

One Redis record per app kind — `AppUpdates.CachedUpdateInfo`, under the
`AppUpdates` key prefix of `RedisDb<UsersDbContext>`, no TTL, no DB and no
backend. It holds three things:

- `Info` — the **announced** build. This is what clients are told, always.
- `PendingInfo` — a build detected in the store that is still waiting out
  `AnnounceDelay`. `ProbeCached` promotes it into `Info` when it comes due.
- `NextCheckAt` — when the store is due to be asked again. Shared, so every node
  re-reads at the same moment rather than drifting on its own timer.

Splitting announced from pending this way keeps the read path free of the
announce window: `GetLatestUpdateInfo` returns `Info` and nothing else. (It used
to be the other way round — `Info` was the newest detection and `PreviousInfo`
was what clients got while it waited — which cost a branch in the read path and a
preservation dance on every write.)

A deserialization failure reads as "nothing cached" rather than faulting the
computed, so a record written in an older shape is simply overwritten by the next
check; that's why the `[Key]` ordinals could be renumbered when the shape changed.

Let `S` be the server's own build version (`ApiConstants.BuildVersion`).
`AppUpdates.GetLatestUpdateInfo`:

1. An `Overrides` entry for the kind wins — that's the QA hook (below).
2. Feature disabled, or the kind has no store id → `null`.
3. `Wasm` → see [Web](#web).
4. `Info.Version >= S` → the release is settled: return it and arm nothing. The
   store already serves everything this server has, so it cannot publish
   anything this server doesn't know about until the next deploy — which
   replaces this process, and with it this cached value.
5. The kind isn't `Android`, Play has a store id, and Play's own record is behind
   `S` → return `Info` and arm nothing, but **read Play's record on the way**,
   which makes this computed a dependent of it. Play publishes before the other
   stores, so until it has the build there is nothing worth asking Apple or
   Microsoft; the next change to Play's record wakes this one up.
6. Otherwise the store is behind this server: start a background check and
   return `Info`. A client older than that release still gets a correct banner
   while a newer build is in review.

Step 4 compares **full build versions**, not trains. Comparing `X.Y` instead —
"the stores publish one build per train" — is what broke Windows on 2026-09-12:
`2.20.109` and `2.20.130` were both live in the Microsoft Store, because a
hotfix ships on the train that is already out there. The record latched onto
`2.20.109`, detected the day before, and clients on `2.20.109` compared
themselves against themselves, so no banner appeared.

Settling on `>= S` is safe because **the deploy precedes the store promotion**:
a release reaches prod before `/promote-release` pushes it to the stores, so `S`
is at or ahead of what any store serves. Should the store somehow get ahead, the
record still moves forward (the check compares against the record, not `S`) and
only then settles. And because `S` changes only by a deploy, which restarts the
process, a settled kind can never stay settled across a release.

### The check

There is no worker and no queue. Step 6 starts `ProbeCached` with
`BackgroundTask.Run`, and **invalidating the computed it was started from is how
the check reports back**. `ProbeCached` returns the cached record either way —
the one another node has just written, or the one this call produced — and the
caller compares its `Info.Version` with what the computation returned:

- Newer → invalidate at once, so this node's clients see the banner immediately.
- Otherwise → invalidate at the record's `NextCheckAt`. That re-runs the method,
  which starts the next check. The loop ends when step 4 settles the kind — which
  is also what keeps it from running forever against a store that never catches
  up.
- The probe threw → invalidate after a fresh `GetRecheckPeriod()`. It's measured
  *after* the failure rather than before the check, because the lock wait plus
  the probe can outlast a whole period, and a moment already in the past re-arms
  with no delay at all.

The comparison is by version rather than by identity: a check that found nothing
still returns a freshly deserialized record holding exactly what the computation
already returned, so reference equality would fire on every empty round.

Such a round recomputes to the value the method already returned, and
`[ComputeMethod(ConsolidationDelay = 0)]` on `IAppUpdates.GetLatestUpdateInfo`
is what keeps those off the wire: consolidation recomputes on the invalidation
and drops it when the output is unchanged, so a client hears only about real
updates rather than once per recheck. That's also why `AppUpdateInfo.Version`
isn't a cached field — see [The contract](#the-contract).

Consolidation also makes invalidation **asynchronous** — an invalidated value
is replaced only once the recompute has finished and differed. Tests therefore
can't read right after `Invalidate()`, and `ComputedTest.When` can't be used to
wait for a side effect or for a value that ends up unchanged; `AppUpdatesTest`
polls instead (`WhenPolled`).

Every node runs this loop, so the cluster is kept to one store hit per period by
an **`IMeshLocks` lock per app kind** (`StoreLocks`, prefix `AppUpdates`) plus
`NextCheckAt` in the record. `ProbeCached` is double-checked locking:

1. Read the record. If `now < NextCheckAt`, someone checked recently — return it
   without taking the lock at all.
2. Take the lock. A node that loses the race waits rather than skipping its turn.
3. Read again. If `now < NextCheckAt`, the winner checked while this one waited —
   return its record, and with it the `NextCheckAt` everyone aligns on.
4. Otherwise probe, write the record with a fresh `NextCheckAt`, release.

Reading the record *inside* the lock is what makes the "is it newer" comparison
and the pending/announced bookkeeping correct: they have to be against what is
actually stored, not against what the computation happened to see before the
probe. A check that finds nothing still rewrites the record — that's what carries
`NextCheckAt` forward.

The announce window lives here too. A detection goes into `PendingInfo`, compared
against `(PendingInfo ?? Info).Version` so a second detection inside the window
supersedes the first; once `DetectedAt + AnnounceDelay` has passed it is promoted
into `Info` and cleared. While one is pending, `NextCheckAt` is pulled in to the
announce moment if the recheck period would land after it — otherwise a 30-minute
period could delay the announcement by half an hour.

One consequence of the probe contract: an app the store doesn't list **throws**,
so `ProbeCached` never reaches the write that would carry `NextCheckAt` forward.
Each node then re-probes once per recheck period instead of one probe
cluster-wide — the lock's dedupe depends on that write. Acceptable at three kinds
and a 3–30 min period, but it's the one case where the guarantee doesn't hold.

`GetRecheckPeriod` widens with how long the wait has already lasted, measured
from node start (i.e. from the deploy that opened the gap): `RecheckPeriods` is
3 min on the first day, 10 min on the second, 30 min from the third on. A store
usually publishes within hours of the deploy, so the early minutes are worth
polling closely; a wait that has already lasted days is unlikely to end this
minute. Volume: at most three store kinds waiting at once, one request each per
period cluster-wide, and the Play gate means the other two are usually not
checked at all.

The Play gate is a heuristic, and it fails in one direction: a build promoted to
the App Store or the Microsoft Store but *not* to Play is never noticed. That
hasn't happened — releases go to all three — and a deliberate single-store
release can be announced with `Overrides`.

### Per store

| Kind | Endpoint | Version |
|---|---|---|
| Ios, MacOS | `itunes.apple.com/lookup?bundleId=…&country=us` | `version` + `currentVersionReleaseDate` |
| Android | `play.google.com/store/apps/details?id=…&gl=US` | the `[[["X.Y.Z"]]]` data block |
| Windows | `displaycatalog.mp.microsoft.com/v7.0/products?bigIds=…&market=US` | max over `Packages[].PackageFullName` |

`AppStoreProbes.Get(appKind)` returns a `StoreProbe` delegate — one `Fetch` plus
one static parser, picked out of two dictionaries keyed by `AppKind` (the URL
format and the parser), so the three stores differ only by those two entries.
`StoreProbeTest` runs each parser on fixtures captured from the live stores.
**Anything unexpected throws**, including an app the storefront doesn't list, so
the check logs and retries rather than reporting "not published". The Play regex
requires exactly one match — every other `X.Y.Z` on that page is review metadata.

Every probe URL carries a `_=<guid>` cache buster, because the App Store lookup is
served by Akamai with `Cache-Control: max-age=86070`. Without it a pod reads
whatever version its edge cached the first time it probed that day, and keeps
reading it for a full day after the release — which is what happened to v2.19.147
on 2026-09-08: the App Store served it at 21:37 UTC, and prod was still reporting
2.18.293 to iOS clients hours and a dozen probes later. A request-side
`Cache-Control: no-cache` does not make Akamai revalidate; only a new cache key
does. Play sends `no-store` and DisplayCatalog only `s-maxage=600`, so the
parameter is redundant there, but all three stores ignore it and a probe that
can't read a stale copy is one less thing to reason about.

All three stores show a full build version, so detection is one comparison:
**published iff the parsed store version is newer than the one the record already
holds**. `S` doesn't enter into *detection* — only into whether probing continues
— because what the store serves is exactly what a client can install, whether or
not the server has moved past it. Requiring `P >= S` (the original rule) meant a
release was recorded only during the window where the store had caught up with
the running server build, so any deploy on top of the release the stores got — a
hotfix, or simply the next train — dropped it for good: `S = 2.19.148` against an
App Store serving `2.19.147` reads as "nothing published", and every client on
`2.18.x` is told about `2.18.x`, i.e. nothing. Comparing against the record
instead makes the detection independent of the deploy cadence.

A version with only two parts (`2.17`, what the App Store showed before v2.19)
names a train, not a build, so `AppStoreProbeResult` reads it as the **last** of
that train: `2.17.9999`. That direction is deliberate — `2.17.0` would be the
lowest, hiding a real update from anyone already on `2.17.x`, whereas `2.17.9999`
at worst offers a banner to someone already on the store's newest build of a
train that predates v2.19. Anything that doesn't parse at all still throws.

`MacOS` reuses the iOS probe: Mac Catalyst is a universal purchase on the iOS App
ID, so the lookup API can't tell the two apart. A Mac user would therefore see
the banner before the Mac build clears review; `AnnounceDelay` is what absorbs
that.

### Web

The WASM bundle is built from the same commit as the server, so there is nothing
to probe — the server *is* the store. Each node answers for itself once it has
been up for `WasmGracePeriod`, and `null` before that: during a rolling deploy a
client on an old pod must not be sent into a reload that can land back on an old
pod.

## The client

`AppUpdateUI` (`UI.Blazor/Services/`, on `UIHub`) compares
`GetLatestUpdateInfo(HostInfo.AppKind)` with
`ApiConstants.BuildVersion` and exposes the tap: `Links.Apps.Store(appKind)`
through `ExternalUrlOpener`, or a `ConfirmModal` + `ReloadUI.Reload()` on the
web. The Windows and macOS links use custom schemes
(`ms-windows-store:`, `macappstore:`) so the tap lands in the store app rather
than a browser; `MauiExternalUrlOpener` routes any non-`http(s)` URL through
`Launcher` because `Browser.Default` rejects those schemes.

`DownloadAppBanner` is a three-state component (`Hidden` / `Install` / `Update`).
Dismissing "Install" never affects "Update" — the two never read each other's
state.

## Configuration (`UsersSettings.AppUpdates`)

| Setting | Default | Purpose |
|---|---|---|
| `IsEnabled` | unset = production instances only | the dev app isn't in any store |
| `AppleStoreId`, `GoogleStoreId`, `MicrosoftStoreId` | prod ids | probe targets; empty disables that kind |
| `RecheckPeriods` | 3 / 10 / 30 min | re-read and probe cadence by day of the wait, last entry repeating |
| `AnnounceDelay` | 1 hour | how long a detected release is held back before clients hear about it |
| `WasmGracePeriod` | 10 min | web rolling-deploy grace |
| `Overrides` | empty | `{ "Android": "2.99.0" }` makes the service report that version for the kind |

`AnnounceDelay` does not apply to `Overrides` — the QA hook answers at once —
and it does not replace `WasmGracePeriod`: the web app is not a store, and its
grace is measured from node start rather than from a detection.

`Overrides` is the QA hook: it works on dev and local instances regardless of
`IsEnabled`, so the banner and both tap paths can be exercised without a real
release.

## The release-process coupling

App Store versions are published under the **full nbgv build version**
(`2.19.40`), not a `X.Y` train — `.github/fastlane/Fastfile`'s
`ensure_app_store_version` defaults to `build_version`. That is what keeps the
lookup API's `version` directly comparable, and detection depends on it: the
`apple-version` workflow input can still override it, but a two-part value there
would read as `X.Y.9999` and offer every client on that train a banner, however
recent their build.
