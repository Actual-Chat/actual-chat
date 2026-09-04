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

`AppUpdateInfo.Version` is the build version (`X.Y.Z`) a client compares itself
against; `StoreVersion` is whatever the store displays (`2.17`, `2.17.246`,
`2.17.246.0`) and is **never** comparable.

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

One Redis record per app kind under `AppUpdates:` in `RedisDb<UsersDbContext>`,
no TTL, no DB and no backend. Let `S` be the server's own build version
(`ApiConstants.BuildVersion`). `AppUpdates.GetLatestUpdateInfo`:

1. An `Overrides` entry for the kind wins — that's the QA hook (below).
2. Feature disabled, or the kind has no store id → `null`.
3. `Wasm` → see [Web](#web).
4. Record's `Info` was detected less than `AnnounceDelay` ago → it's pending:
   arm a re-read for the moment it comes due and return `PreviousInfo`, the
   release `Info` replaced. Clients behind that one keep their banner; nobody
   hears about the new one yet. Nothing needs probing while a detection is
   pending, so this returns here.
5. Record's `Info.Version >= S` → the release is settled: return it and arm
   nothing. A published release is assumed to stay published, so this value is
   cached until the process is replaced by the next deploy.
6. Otherwise the server is ahead of the store: ask the prober to work on the
   kind, arm a re-read in `RecheckPeriod`, and return `Info`. A client older
   than that release still gets a correct banner while the newer build is in
   review.

A server deploy inside a pending hour resolves itself: once the hour is out,
step 5 sees `Info.Version < S` and step 6 resumes probing, and the next
detection moves the pending release into `PreviousInfo` where it belongs.

`AppUpdateProber` is an `ActivatedWorkerBase` singleton on the API hosts. Per due
entry it re-reads the record (another node may have settled it), takes a
`SET AppUpdates:probe:{kind} 1 NX PX MinProbeInterval` throttle — a
throttle, not a lock, because probes are idempotent — probes, and either writes
the record and drops the entry or backs off along `ProbeDelays`. A publish also
invalidates this node's own computed, so its clients flip at once; other nodes
learn on their next `RecheckPeriod` re-read.

Volume: three store kinds, so the steady state between a deploy and the last
store publish is at most three requests per 30 minutes cluster-wide.

### Per store

| Kind | Endpoint | Version |
|---|---|---|
| Ios, MacOS | `itunes.apple.com/lookup?bundleId=…&country=us` | `version` + `currentVersionReleaseDate` |
| Android | `play.google.com/store/apps/details?id=…&gl=US` | the `[[["X.Y.Z"]]]` data block |
| Windows | `displaycatalog.mp.microsoft.com/v7.0/products?bigIds=…&market=US` | max over `Packages[].PackageFullName` |

Each probe is HTTP plus a static parser, so `StoreProbeTest` runs it on
fixtures captured from the live stores. An app the storefront doesn't list reads
as `null`; **anything unexpected throws**, so the prober logs and retries rather
than reporting "not published". The Play regex requires exactly one match — every
other `X.Y.Z` on that page is review metadata.

Two store families:

- **Full-version stores** (Play, Microsoft, and the App Store under the policy
  below): published iff the parsed store version `P >= S`. Exact, no history. A
  store *ahead* of the server (a rollback) still moves the record forward, which
  is right — the store build is what users can get.
- **Train-only** (an App Store record showing `2.17`): the store version can't be
  compared with `S`, so the rule is change detection on the server's train. With
  no prior record, store a baseline and announce nothing — `2.17` could be
  `2.17.100`. From then on, when `(version, currentVersionReleaseDate)` differs
  from the baseline *and* the store's train is `>= S`'s train, record
  `Info.Version = S`.

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
| `RecheckPeriod` | 2 min | compute-method re-read while unsettled |
| `AnnounceDelay` | 1 hour | how long a detected release is held back before clients hear about it |
| `ProbeDelayMin` / `ProbeDelayMax` | 60 s / 1800 s | per-entry backoff |
| `MinProbeInterval` | 50 s | cluster dedupe window |
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
lookup API's `version` directly comparable and the train-only path unused for
anything published from now on. The `apple-version` workflow input still
overrides it, but only for a deliberate exception.

The train-only path stays implemented because the record published before this
change shows `2.17`, and a two-part version takes that path whatever the setting
says.
