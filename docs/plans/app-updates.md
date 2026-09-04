# App updates — the "Update Voxt" banner

## Goal

Tell users that a newer build of the app they are running is available, and take
them to it in one tap. The existing "Install Voxt" banner (settings panel + left
panel) becomes dual-purpose: when an update is out it reads **Update Voxt**, is
never dismissable, shows on every host including mobile, and opens the right
store page (or reloads the web app after a confirmation). Otherwise it behaves
exactly as today.

The hard part is not the banner; it's knowing, per app kind, that the store
actually serves the new build. The server deploys hours to days before the
stores do, so "server version > client version" is the wrong signal: it would
send users to a store page with no update on it.

## Summary

- A new API service, `IAppUpdates.GetLatestUpdateInfo(appKind)`, returns the
  newest build known to be **published in the store** for that kind, or `null`
  when we don't know yet. Clients compare it with their own version and show the
  banner when they're behind.
- Detection lives on the API hosts and is stored in Redis, one record per kind,
  no backend or DB. Probing starts only when the server's own version is ahead
  of the stored record, backs off exponentially, and stops for good once the
  store shows the build — a published release is assumed to stay published.
- A detected release is announced only `AnnounceDelay` (1 hour) after it was
  detected; until then clients are told about the release it replaced.
- Each store is probed through a public, unauthenticated endpoint; all three
  were verified today (see [Evidence](#evidence)). The web app needs no probe:
  the server *is* its store.
- The App Store exposes only the marketing version string, not the build.
  Decided (2026-09-03): from the next release on, App Store versions are
  published under the full nbgv version (`2.19.40`), which the Fastfile already
  accepts, so the lookup API becomes exact. A train-only fallback covers the
  currently published `2.17` record until then.
- The banner rework is client-only UI on top of the compute method; the web
  "Update" path is a `ConfirmModal` + `ReloadUI.Reload()`, the app path is an
  external store link.

## What exists today

**The banner.** `UI.Blazor/Components/DownloadAppBanner/DownloadAppBanner.razor`
is a `ComputedStateComponent<UIHub, bool>` whose state is "hidden". It's hosted
twice: `SettingsPanel.razor:46` with `CanBeClosed="false"` and
`LeftPanelContent.razor:37` with `CanBeClosed="true"`. Its rules: hidden on MAUI
mobile always; on MAUI desktop only the non-closable settings-panel copy shows;
on the web both show until dismissed. Dismissal is a
`StoredState<Box<bool>>` in `LocalSettings` under `DownloadAppBanner.IsDismissed`.
Click opens `DownloadAppModal` (the download tiles). The title is
`L.Download_GetAppBanner_Format` ("Install {0}").

**Version plumbing.** `ApiConstants` (`Api.Contracts/ApiConstants.cs`) gives every
host its own `VersionString` (`X.Y.0.0`, assembly version) and
`FullVersionString` (nbgv informational version: `2.17.246+sha` on a release
branch, `2.19.6-alpha+sha` on `dev`). `ISystemProperties.GetServerApiInfo` returns
the server's strings plus a `CompatibilityLevel`, and `ClientUpgradeCover.razor`
already turns `Incompatible` into a full-screen "Please update" cover with a
store link (`Links.Apps.*`) on MAUI and `ReloadUI.Reload()` on the web. This
plan covers the other case: compatible but behind.

**Release flow.** A push to `release/vX.Y` deploys the prod server and *stages*
the apps (Play internal track, TestFlight, a pending Microsoft Store submission).
`/promote-release` later dispatches `promote-release.yml`, which promotes Play to
production (immediate, with optional rollout %), submits iOS and Mac Catalyst
for App Store review (released `AFTER_APPROVAL`, typically hours to a day
later), and commits the Microsoft Store submission to certification. So there
is always a window in which the server is ahead of every store, and a longer
one in which it is ahead of the App Store only.

**Region.** MAUI has `DeviceRegionProvider.GetDeviceRegion()` (SIM → network →
locale, `App.Maui/Services/DeviceRegionProvider.cs`), used by `MauiContacts`.
The server has `GeoIP.ToCountryCode(ip)` (`Core.Server/Geo/GeoIP.cs`). Neither
is used by this feature — see [No region dimension](#no-region-dimension).

**Redis.** Users.Service already has `RedisDb<UsersDbContext>` (registered in
`UsersServiceModule.cs:267`) and uses it for TOTP throttles (`EmailAuth`,
`PhoneAuth`, `TotpCodes`). `RedisSerializer.Default` (MessagePack) converts
values to/from `RedisValue`. `RedisMeshLocks` exists but is heavier than this
feature needs.

## Evidence

All probes below were run on 2026-09-03 against the production app ids. The
current prod release visible in the stores is `2.17.246`.

### App Store — iTunes Lookup API

`GET https://itunes.apple.com/lookup?bundleId=chat.actual.app&country=us`

```json
{ "resultCount": 1, "results": [ {
  "kind": "software", "version": "2.17",
  "currentVersionReleaseDate": "2026-08-31T01:20:07Z",
  "minimumOsVersion": "16.4", "features": ["iosUniversal"],
  "trackViewUrl": "https://apps.apple.com/us/app/voxt/id6450874551?uo=4" } ] }
```

- `country=de` returns the same record with a `/de/` URL; a storefront where the
  app isn't sold returns `resultCount: 0`.
- `version` is the **App Store version string** the promote workflow chose
  (`2.17`), not the build (`2.17.246`). `.github/fastlane/Fastfile` documents
  this: builds carry the nbgv version, App Store versions are a train `X.Y`
  reused/renamed per release, and `apple-version` is an optional workflow input
  that overrides the string.
- `entity=macSoftware` and `id=6450874551` return the *same* record: the Mac
  Catalyst app is a universal purchase on the iOS App ID
  (`docs/maccatalyst-distribution.md`), so the lookup API can't tell whether the
  Mac build has cleared review. The web page
  `apps.apple.com/us/app/voxt/id6450874551?platform=mac` does render a
  Mac-specific "Version 2.17" from its `serialized-server-data` JSON, but both
  platforms are on `2.17` today, so this wasn't proven platform-specific.

### Google Play — store page

`GET https://play.google.com/store/apps/details?id=chat.actual.app&hl=en&gl=US`
(with a browser `User-Agent`) returns 1.1 MB of HTML containing exactly one
`[[["2.17.246"]],[[[…` block — the "About this app → Version" data, i.e. the
`versionName`, which nbgv sets to the full build version. Other `"X.Y.Z"`
strings on the page are review metadata (`2.5.239`, `2.0.335`, …), so the probe
must key on the `[[["…"]]]` shape, not on any version-looking token. `gl=`
selects the storefront country. There is no public JSON API; the alternatives
are the Play Developer API (needs a publisher service-account key on the
server) or the Play In-App Updates SDK on the device (see
[Rejected / deferred](#rejected--deferred)).

### Microsoft Store — DisplayCatalog

`GET https://displaycatalog.mp.microsoft.com/v7.0/products?bigIds=9N6RWRD9FMS2&market=US&languages=en-US`

returns JSON whose `DisplaySkuAvailabilities[].Sku.Properties.Packages[]` lists
`PackageFullName`s — today `ActualChatInc.ActualChat_2.16.608.0_x64__kpmvmkx3s0ak6`
and `ActualChatInc.ActualChat_2.17.246.0_x64__kpmvmkx3s0ak6` — plus
`Availabilities[].Markets` and `LastModifiedDate` (`2026-08-28T21:39Z`). The
MSIX version is the nbgv version with a `.0` suffix, so it's directly
comparable. `market=` selects the region. (`storeedgefd.dsx.mp.microsoft.com/v9.0/products/…`
also answers but carries no package version, so DisplayCatalog is the one to use.)

### Web

Nothing to probe. The WASM bundle is built from the same commit as the server,
so `ApiConstants.FullVersionString` on the client equals the server's when the
client is current. During a rolling deploy some pods are old and some new; the
only thing the feature needs there is a short grace period so the banner
doesn't flap.

## Design

### Contract

`Api.Contracts/Users/IAppUpdates.cs`, next to `ISystemProperties`:

```csharp
public interface IAppUpdates : IComputeService
{
    [ComputeMethod]
    Task<AppUpdateInfo?> GetLatestUpdateInfo(AppKind appKind, CancellationToken cancellationToken);
}

[DataContract, MessagePackObject]
public sealed partial record AppUpdateInfo(
    [property: DataMember, Key(0)] AppKind AppKind,
    [property: DataMember, Key(1)] string Version,
    [property: DataMember, Key(2)] string StoreVersion,
    [property: DataMember, Key(3)] Moment ReleasedAt,
    [property: DataMember, Key(4)] Moment DetectedAt);
```

- `Version` is the build version (`X.Y.Z`) the client compares itself against.
  `StoreVersion` is whatever the store displays (`2.17`, `2.17.246`,
  `2.17.246.0`) — for logs and the modal, never for comparison.
- `ReleasedAt` is the store's own release date where it has one (App Store
  `currentVersionReleaseDate`, DisplayCatalog `LastModifiedDate`), else
  `DetectedAt`.
- `null` means *unknown* — no banner. It's the answer on non-production
  instances, for `AppKind.Unknown`, during the web grace period, and until the
  first store publish after this feature ships.

The client is registered in `ApiContractsModule` with `fusion.AddClient<IAppUpdates>()`;
the default remote-computed cache mode applies, so MAUI clients get the last
answer from Kvasar at startup and refresh once connected.

### The record and the detection rule

Redis key `AppUpdates:{kind}` in `RedisDb<UsersDbContext>`, no TTL, holding:

```csharp
[DataContract, MessagePackObject]
public sealed partial record AppUpdateRecord(
    [property: DataMember, Key(0)] AppUpdateInfo? Info,
    [property: DataMember, Key(1)] string LastSeenStoreVersion,
    [property: DataMember, Key(2)] Moment LastSeenReleasedAt,
    [property: DataMember, Key(3)] Moment ProbedAt,
    [property: DataMember, Key(4)] AppUpdateInfo? PreviousInfo = null);
```

Let `S` be the server's own build version (`ApiConstants.FullVersionString`
with the `-alpha`/`+sha` tail stripped: `2.18.12`). The compute method:

1. An `Overrides` entry for the kind → return it at once, no delay.
2. Feature disabled for this kind (non-production instance, or the kind has no
   store id configured) → `null`.
3. `kind == Wasm` → see [Web](#web-1); no Redis, and `AnnounceDelay` doesn't
   apply — that path has its own `WasmGracePeriod`.
4. Read the record. If `record.Info` was detected less than `AnnounceDelay` ago
   it is pending: arm the invalidation for the moment it comes due and return
   `record.PreviousInfo`. Nothing needs probing while a detection is pending.
5. If `record.Info.Version >= S` the release is settled: return `record.Info`
   and **do not** schedule invalidation — this value is cached until the
   process is replaced by the next deploy.
6. Otherwise the server is ahead of the store. Ask the prober to work on the
   kind, arm `Computed.GetCurrent().Invalidate(RecheckPeriod)` so this node
   re-reads Redis in a couple of minutes, and return `record?.Info` — the
   *previous* release. A client older than that still gets a correct banner
   while the newer build is in review.

A server deploy inside a pending window resolves itself: once the window is
out, step 5 sees `Info.Version < S` and step 6 resumes probing; the next
detection moves the pending release into `PreviousInfo`.

The prober decides "published" per store family:

- **Full-version stores (Play, Microsoft, and the App Store under the
  build-version policy below):** published iff the parsed store version
  `P >= S`. Record `Info.Version = P`. Exact; needs no history. If the store is
  ahead of the server (a rollback), the record still moves forward, which is
  right: the store build is what users can get.
- **Train-only store (App Store with `X.Y` strings):** the store version can't
  be compared with `S` directly, so the rule is *change detection on the
  server's train*. With no prior record, store a baseline
  (`Info = null`, `LastSeen* = what the store shows`) and announce nothing —
  we can't tell whether `2.17` is `2.17.246` or `2.17.100`. From then on, when
  `(version, currentVersionReleaseDate)` differs from `LastSeen*` and the
  store's train is `>= S`'s train, record `Info.Version = S`,
  `StoreVersion = P`, `ReleasedAt = currentVersionReleaseDate`. The known
  imprecision: if two server deploys happen inside one review window, the
  record names the later one and users on the earlier build get a banner that
  leads to an "Open" button. That's the reason for D1 below.

Once `Info.Version >= S` the entry is dropped from the prober; nothing re-checks
a published release. The next server deploy restarts the cycle by making `S`
larger than the record.

### Store probes

One `IStoreProbe` per kind in `Users.Service/AppUpdates/Stores/`, each a
small class over a named `HttpClient` (`services.AddHttpClient(AppUpdateProber.HttpClientName)`,
the `EmbeddingsCalculator` pattern) returning
`StoreProbeResult(string StoreVersion, Version? BuildVersion, Moment? ReleasedAt)`
or `null` when the app isn't listed:

| Kind | Endpoint | Version parsing |
|---|---|---|
| Ios, MacOS | `itunes.apple.com/lookup?bundleId=chat.actual.app&country=us` | `version` → `BuildVersion` if it has three parts (the build-version policy), else train-only; `currentVersionReleaseDate` |
| Android | `play.google.com/store/apps/details?id=chat.actual.app&hl=en&gl=US` | regex `\[\[\["(\d+\.\d+\.\d+)"\]\]` — exactly one match today; 0 or >1 matches = parse failure, not "unpublished" |
| Windows | `displaycatalog.mp.microsoft.com/v7.0/products?bigIds=9N6RWRD9FMS2&market=US&languages=en-US` | max version over `Packages[].PackageFullName` (`_2.17.246.0_`), 4th part dropped; `LastModifiedDate` |

Probes send a browser-like `User-Agent` (Play serves a consent stub otherwise),
cap the response at a few MB, and treat any HTTP/parse failure as *retry
later* with a warning — never as "not published". Each adapter is a pure
function of the response body, so the unit tests run on fixtures captured from
today's probes.

`MacOS` reuses the iOS probe (same App Store record). The plan accepts that a
Mac user may see the banner before the Mac build clears review; the
`?platform=mac` page is the refinement if it needs solving properly.

### No region dimension

Revised 2026-09-03: there is none. The probes ask the US storefront
(`country=us`, `gl=US`, `market=US`) and the answer stands for every user.

Both stores publish to every storefront at once — a staged rollout is a
percentage of users, not a set of countries — Voxt is listed in every
storefront we sampled, and the device region (SIM → network → locale) is only a
rough proxy for the *account's* storefront, which is what actually selects what
a user is offered. So per-region records and probes cost complexity on every
layer (contract, Redis keys, prober entries, `HostInfo`, a telephony call on
the Android cold-start path) and buy nothing measurable.

### Announce delay

What per-region probing was really buying was *not announcing a release to
someone who can't get it yet*, so that is what the design keeps — as a wait
rather than as a dimension. A detected release is announced only once
`AnnounceDelay` (default 1 hour) has passed since `Info.DetectedAt`; until then
`GetLatestUpdateInfo` returns `PreviousInfo`, the release it replaced, so a
client behind *that* one keeps its banner and nobody is sent to a store page
that hasn't caught up.

It covers propagation lag between storefronts, a rollout that is still ramping,
and the gap between an iOS release and the Mac build sharing its App ID (which
is why `MacOSExtraDelay` is gone). It does **not** apply to `Overrides`, and it
does not replace `WasmGracePeriod`: the web app is not a store, and that grace
is measured from node start rather than from a detection.

### Who probes, and how nodes agree

`AppUpdateProber` is a `WorkerBase` singleton on API hosts (registered in the
`rpcHost.IsApiHost` branch of `UsersServiceModule`, started as a hosted service
like `ContactGreeter`). `Request(kind)` adds to a
`ConcurrentDictionary<AppKind, ProbeState>` and wakes the loop; the
loop uses `AsyncChain.From(ProbeDue).Log(...).RetryForever(...).CycleForever()`.

Per due entry:

1. Re-read the record; if another node already settled it, drop the entry.
2. Cluster dedupe: `SET AppUpdates:probe:{kind} 1 NX PX {MinProbeInterval}`.
   If the key exists, someone probed within the last minute — skip this turn.
   A throttle, not a lock: probes are idempotent, so two nodes racing once is
   harmless. This is why `RedisMeshLocks` isn't needed.
3. Probe. Published → write the record, drop the entry, and invalidate this
   node's own `GetLatestUpdateInfo(kind)` inside `Invalidation.Begin()`
   so its clients flip at once. Not yet → next attempt after
   `ProbeDelays.Next()` (`RetryDelaySeq.Exp(60, 1800)`: 1 min → 30 min cap).
   Failure → same backoff, `LogWarning`.

Other nodes learn about the write through the `RecheckPeriod` re-read armed in
step 4 of the compute method (default 2 min). A Redis pub/sub fan-out
(`RedisSubscription` exists) would make it instant, but a two-minute lag on a
release banner isn't worth a second mechanism. If `RecheckPeriod` ever needs to
drop, that's the upgrade path.

Volume check: three store kinds, so the steady state between a deploy and the
last store publish is at most three requests per 30 minutes cluster-wide.

### Web

`GetLatestUpdateInfo(Wasm, _)` on a node returns
`AppUpdateInfo(Wasm, "", S, ApiConstants.FullVersionString, ReleasedAt: nodeStartedAt, DetectedAt: nodeStartedAt)`
once the node has been up for `WasmGracePeriod` (default 10 min), and `null`
before that with `Computed.GetCurrent().Invalidate(remaining)`. Each node
answers for itself: during a rolling deploy a client on an old pod sees no
update, reconnects to a new pod when the old one drains, and then sees it;
the grace stops the banner from appearing while a reload could still land on
an old pod. No Redis, no probe.

### Client

`AppUpdateUI` in `UI.Blazor/Services/` (shared by web and MAUI; exposed on
`UIHub` like `ReloadUI`), an `IComputeService` with:

```csharp
[ComputeMethod]
public virtual async Task<AppUpdateInfo?> GetAvailableUpdate(CancellationToken cancellationToken)
{
    var info = await AppUpdates.GetLatestUpdateInfo(HostInfo.AppKind, cancellationToken).ConfigureAwait(false);
    if (info is null || !VersionExt.TryParseBuildVersion(info.Version, out var latest))
        return null;
    return latest > OwnVersion ? info : null;
}

public Task Update()
    => HostInfo.AppKind == AppKind.Wasm
        ? ConfirmAndReload()
        : ExternalUrlOpener.Open(Links.Apps.Store(HostInfo.AppKind));
```

`OwnVersion` is `ApiConstants.FullVersionString` parsed the same way as `S` on
the server, through the one shared helper.

`DownloadAppBanner` becomes a three-state component
(`enum Mode { Hidden, Install, Update }`) instead of a bool:

- `Update` wins over everything: it ignores `_isDismissed` and `CanBeClosed`,
  renders on MAUI mobile, and has no close button. Title
  `L.AppUpdate_Banner_Format` ("Update {0}"), the existing gradient style, an
  update icon in place of `icon-download`.
- Otherwise the current logic decides between `Install` and `Hidden`,
  untouched, including the dismissal flag. Dismissing "Install" never affects
  "Update" because the two never read each other's state.
- `ConfirmAndReload` shows
  `ConfirmModal.Model(IsDestructive: false, L.AppUpdate_ReloadText, () => ReloadUI.Reload())`
  with `Title = L.AppUpdate_ReloadTitle_Format(AppName)` and
  `ConfirmButtonText = L.Common_Update`. `ReloadUI` is already virtual and
  MAUI-overridden, so the same call is right on every host if the web path is
  ever reused there.

Store links: add `Links.Apps.Store(AppKind)` returning the deep link per kind
and keep the existing constants for the landing page:

| Kind | Link | Notes |
|---|---|---|
| Android | `https://play.google.com/store/apps/details?id=chat.actual.app` (existing) | Android routes it to the Play app; `market://` gains nothing and would need `Launcher` instead of `Browser` |
| Ios | `Links.Apps.iOS` (existing, `apps.apple.com/us/app/actual-chat/id6450874551`) | iOS opens the App Store app |
| MacOS | `macappstore://apps.apple.com/app/id6450874551` | `MauiBrowser.Open` uses `UIApplication.OpenUrl` on Catalyst, which accepts custom schemes; the `https` link would open Safari instead of the Mac App Store |
| Windows | `ms-windows-store://pdp/?productid=9N6RWRD9FMS2` | opens the Store app on the product page; `Browser.Default.OpenAsync` may reject non-http schemes on Windows, in which case `MauiExternalUrlOpener` falls back to `Launcher.Default.OpenAsync` for them |

The two non-`https` links are the only "verify on device" items on the client
side.

### Configuration, gating, QA

`UsersSettings.AppUpdates` (`AppUpdateSettings`):

| Setting | Default | Purpose |
|---|---|---|
| `IsEnabled` | `HostInfo.IsProductionInstance` | dev/local instances answer `null`; the dev app (`chat.actual.dev.app`) isn't in any store |
| `AppleStoreId`, `GoogleStoreId`, `MicrosoftStoreId` | prod ids | probe targets; empty disables that kind |
| `RecheckPeriod` | 2 min | compute-method re-read while unsettled |
| `AnnounceDelay` | 1 hour | how long a detected release is held back before clients hear about it |
| `ProbeDelays` | `Exp(60, 1800)` s | per-entry backoff |
| `MinProbeInterval` | 50 s | cluster dedupe window |
| `WasmGracePeriod` | 10 min | web rolling-deploy grace |
| `Overrides` | empty | `{ "Android": "2.99.0" }` makes the service return that version for the kind — the QA hook for dev and local, so the banner and both click paths can be exercised without a real release |

### Localization

New `AppUpdate_` group in `Strings.en.json` and all 22 real-language catalogs,
with typed members in `LocalizedStringsLocalizerExt.cs`, then the BCMS and Max
regeneration per `docs/i18n.md`:

- `AppUpdate_Banner_Format` — "Update {0}"
- `AppUpdate_ReloadTitle_Format` — "Update {0}?"
- `AppUpdate_ReloadText` — "The page will reload to load the latest version."

Buttons reuse `Common_Update` and `Common_Cancel`.

## Decisions

Each item lists the options weighed and the pick. D1 changes the release
process and was confirmed by the user on 2026-09-03; the rest are engineering
calls that are cheap to reverse.

**D1 — App Store version strings carry the build version.** Options: (a) keep
train strings (`2.17`, `2.17.1`) and use change detection with `Version = S`;
(b) publish under the full nbgv build version (`2.17.246`), making the lookup
API's `version` directly comparable. **Decided: (b).** It's a default-value
change in the Fastfile, the workflow input description and the skill (the
Fastfile already accepts any string), Apple allows three-component versions,
the App Store then shows the same version Play and the Microsoft Store show,
and it removes the only imprecise path in the design. The train-only path
stays implemented because the currently published `2.17` record has to be
handled until the first release under the new policy is out; a two-part
`version` always takes it.

**D2 — Detection is server-side, not on-device.** Android has the Play In-App
Updates SDK and Windows has `StoreContext.GetAppAndOptionalStorePackageUpdatesAsync`,
both official and rollout-aware. Pick server-side anyway: one place decides,
one code path for all four kinds plus web, probes happen once per release
rather than once per device, no new native packages in the APK (the bundle
work in `app-bundles.md` is fighting for every MB), and the dev app can be
driven by `Overrides`. The SDKs remain the upgrade if per-device rollout
accuracy is ever needed — the client-side `AppUpdateUI` boundary is where
they'd plug in.

**D3 — Play is probed through the store page, not the Developer API.** The
Developer API is authoritative but needs a publisher service-account key on
the prod server and still doesn't reflect Google's post-promotion review
delay; the public page reflects what a user in `gl=` actually sees. The regex
is narrow (`[[["X.Y.Z"]]]`, exactly one match required) and a parse failure is
logged and retried, never interpreted.

**D4 — Region comes from the client, not GeoIP.** Storefronts follow the
account country; the device region (SIM first) is the closest observable
proxy, and the server-side `GeoIP` would be wrong for every VPN user. Web
clients don't need one.
*Superseded 2026-09-03: there is no region at all — see D5.*

**D5 — Per-region probing, lazy.** Over "US result + fixed delay for others":
the endpoints support it for free and the cost is bounded by active regions.
*Superseded 2026-09-03: the rejected option is the one we took.* Both stores
publish to every storefront at once (a staged rollout is a percentage of users,
not a set of countries), Voxt is listed in every storefront we sampled, and the
device region is only a rough proxy for the account's storefront — so the
per-region machinery bought nothing. One US probe per kind, and the
propagation lag it would have covered is covered by the wait instead.

**D6 — Throttle key, not a mesh lock; recheck period, not pub/sub.** Probes
are idempotent and a two-minute cross-node lag is fine for a release banner.
Both upgrades are local changes if a need appears.

**D7 — `AppUpdateInfo` is version-agnostic; the client compares.** The
alternative (`IsUpdateAvailable(kind, clientVersion)`) fragments the cache per
client version for no gain. One record per kind is also what a MAUI client
caches offline.

**D8 — The web app goes through the same service.** `GetServerApiInfo` already
carries the server version, so a web-only banner could skip `IAppUpdates`
entirely. Routing it through the service keeps one client abstraction, and
puts the rolling-deploy grace where it belongs (per node, server-side).

**D9 — Keep the component name.** `DownloadAppBanner` gains a mode instead of
being split or renamed; both host pages stay as they are. A rename touches
the generated AOT source for no behavioral gain.

## Rejected / deferred

- **Push or toast on release.** Out of scope; the banner is the deliverable.
  `INotifications` could carry it later off the same record.
- **CI writing the record directly** (the promote workflow knows when it
  promoted). It can't reach prod Redis, doesn't know when App Store review
  ends, and would make the server trust an external writer. Probing is
  self-contained.
- **App Store Connect API from the server** for Mac/iOS build numbers. Needs
  the API key on the server; D1 makes it unnecessary.
- **Re-probing settled releases.** Per the goal statement: published stays
  published. A store-side pull of a build is a manual incident, not a code
  path.

## Reuse

**Existing abstractions to reuse:**

- `ISystemProperties` / `ServerApiInfo` / `ApiConstants` — the version
  vocabulary; `IAppUpdates` sits next to them in `Api.Contracts/Users/`.
- `ClientUpgradeCover.razor` — the pattern for "store link on MAUI, reload on
  web", and `Links.Apps.*` for the links themselves.
- `DownloadAppBanner.razor`, its CSS, `StoredState<Box<bool>>` over
  `LocalSettings` — extended, not replaced.
- `ReloadUI` / `MauiReloadUI`, `ExternalUrlOpener` / `MauiExternalUrlOpener` /
  `MauiBrowser`, `ConfirmModal.Model` — the two click paths need nothing new.
- `RedisDb<UsersDbContext>`, `RedisSerializer.Default`, `StringSetAsync(..., When.NotExists)`
  as in `EmailAuth.IsThrottled` — storage and the dedupe key.
- `WorkerBase` + `AsyncChain` + `RetryDelaySeq` (coding-style "Background
  Workers"); `ContactGreeter` as the hosted-service registration example.
- `Computed.GetCurrent().Invalidate(delay)` — the timed re-read, as in
  `LiveTime.GetDeltaText` and `Invites`.
- `services.AddHttpClient(name)` + `IHttpClientFactory` — as `EmbeddingsCalculator`.
- `HostInfo`, `AppKindExt`, `UsersSettings`, `HostModule<TSettings>` — host
  facts, kind predicates, settings binding.
- `L.*` typed localizer members, `AppLocalizationTest` — new strings follow
  the i18n rules verbatim.
- `AnalyticEvents` — optional "banner shown / tapped" events; not required.

**New components and where they belong:**

- `IAppUpdates`, `AppUpdateInfo` → `Api.Contracts` (shared contract, like
  `ISystemProperties`).
- `VersionExt.TryParseBuildVersion(string, out Version)` — strips the nbgv
  `-alpha`/`+sha` tail and normalizes to three parts. Used by the server
  service, the client, and it replaces the inline parse in
  `SystemProperties.GetServerApiInfoNC`. → `ActualChat.Core`.
- `AppUpdateUI` → `UI.Blazor/Services` (both hosts render the banner).
- `Links.Apps.Store(AppKind)` → `Api/Links.cs`, beside the constants.
- `AppUpdates`, `AppUpdateProber`, `AppUpdateRecord`, `IStoreProbe` and the
  three probes, `AppUpdateSettings` → `Users.Service/AppUpdates/`. Local on
  purpose: nothing else needs a store probe, and a server-side singleton with
  Redis fits Users.Service, which already owns `SystemProperties`.
- A typed `RedisDb.Get<T>(key)` / `Set<T>(key, value)` pair over
  `RedisSerializer` — only `RedisMultiHashMap` and `RedisScope` wrap it today
  and neither is a plain key/value. If it's written generically (it should
  be), it goes to `ActualChat.Redis/RedisDbExt.cs`, not into the feature.

## Plan

Each step builds and its tests pass on its own; the branch stays deployable
between them.

### 1. Contract and shared helpers

- `Api.Contracts/Users/IAppUpdates.cs` (interface + `AppUpdateInfo`), client
  registration in `ApiContractsModule`.
- `Core/VersionExt.cs`; switch `SystemProperties.GetServerApiInfoNC` to it.
- `AppUpdateSettings` on `UsersSettings`.
- Unit tests: `VersionExt` on `2.17.246+sha`, `2.19.6-alpha+sha`, `2.17`,
  `2.17.246.0`, garbage.

### 2. Store probes

- `IStoreProbe`, `StoreProbeResult`, `AppleStoreProbe`,
  `GoogleStoreProbe`, `MicrosoftStoreProbe`; named `HttpClient`.
- Fixtures under `tests/Users.UnitTests/AppUpdates/Fixtures/` captured from
  today's probes (lookup JSON, the Play page's data block, DisplayCatalog
  JSON, plus an empty-storefront lookup).
- Unit tests per probe: version and date extraction, the storefront in the
  request URL, an unlisted app → `null`, parse failure → throws (so the
  prober logs and retries).

### 3. Server service

- `AppUpdateRecord`, `AppUpdateStore` (typed Redis get/set + the NX throttle),
  `AppUpdates` (the compute method), `AppUpdateProber` (worker), module
  registration under `IsApiHost`, `Overrides` handling.
- `Users.IntegrationTests/AppUpdatesTest` with a scripted `IStoreProbe`
  replacing the real ones: `null` until published; flips within
  `RecheckPeriod` after the probe reports the build; previous release is
  returned while the new one is pending; settled records are never probed
  again; train-only path baseline → change → announce; a detection held back
  for `AnnounceDelay` and announced after it; probing resuming when a pending
  window ends with the server still ahead; `Wasm` grace; non-production →
  `null`.

### 4. Client

- `AppUpdateUI`, `UIHub` accessors, `Links.Apps.Store`.
- `DownloadAppBanner` three-state rework and CSS for the update icon.
- Localization: three keys × 22 catalogs, typed members, BCMS/Max regen,
  `AppLocalizationTest` green.
- Manual QA on local with `Overrides`: web confirm → reload; Windows deep
  link; iOS/macOS/Android deep links on device (the `/macmini` and `/ios-run`
  skills).

### 5. Release process (D1)

- `.github/fastlane/Fastfile`: when `version:` is empty,
  `ensure_app_store_version` uses `build_version` instead of deriving a train;
  the train logic is removed rather than kept as dead code.
- `promote-release.yml`: the `apple-version` input description says it
  defaults to the build version and exists only for a deliberate override.
- `/promote-release` skill: step 3 stops asking for an App Store version and
  the pitfalls note that the store version equals the build version from now
  on.
- No switch is needed for the transition: a two-part `version` (today's `2.17`)
  takes the train-only path and a three-part one is the build.

### 6. Docs

- `docs/api-index.md`: `IAppUpdates`, `AppUpdateInfo`, `AppUpdateUI`,
  `VersionExt`.
- Remove this plan from `docs/plans/index.md` when it ships; the design notes
  above move to a short `docs/app-updates.md` if anything non-obvious remains.

## Open questions

1. Should the Mac banner wait for anything beyond the iOS release? Answered
   2026-09-03: the general announce delay covers it, and `MacOSExtraDelay` is
   gone.
2. `WasmGracePeriod` of 10 minutes assumes a rolling deploy finishes well
   inside that; if prod rollouts take longer, the number should follow.
3. Should banner impressions and taps be tracked through `AnalyticEvents`? It
   would tell us how fast users move after a release; cheap to add in step 4.
   Not in the first cut.

## Related

- `docs/plans/notification-lifecycle.md` — if a release ever becomes a push,
  it goes through that machinery.
- `docs/plans/android-anr-issues.md` — the cold-start budget this feature
  must respect; dropping the region is what keeps it off that path entirely.
- `docs/maccatalyst-distribution.md`, `.github/fastlane/Fastfile`,
  `.claude/skills/promote-release/SKILL.md` — the release side of D1.
