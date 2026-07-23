# On-Premises Instances Plan

## Overview

Allow customers to run their own Voxt instance — their own server, databases,
Redis, NATS, and (optionally) their own transcription/LLM providers — while
still using our official mobile/desktop apps to access it. The customer owns
their data completely: nothing flows to our cloud except what they explicitly
opt into (registration, push relay, "Sign in with Voxt"). An instance can host
a place or a set of places; our apps show content from the user's cloud account
and from all connected on-prem instances side by side, talking to each server
independently.

Goals:

1. **Data sovereignty** — no user content ever transits our cloud; every
   external dependency (transcription, LLM, email, storage) is configurable or
   disableable per instance.
2. **First-class app experience** — our apps connect to cloud + N instances
   simultaneously and merge the content; account linking makes this feel like
   one product, not N logins.
3. **Registration** — every instance is known to us: it registers once,
   receives an `InstanceId` and a key pair, and those keys are what make it
   connectable/verifiable from our apps and usable with our push relay and
   identity services.
4. **Convenience** — install is a docker-compose bundle; connecting the app is
   a QR code / invite link.

Non-goals (for now):

- Server-to-server federation (cross-instance peer chats, cross-instance
  message delivery). The design leaves the door open but ships nothing.
- Multi-tenant cloud (one server hosting many isolated tenants). Instances are
  physically separate deployments.
- On-prem web client hosting restrictions — the instance serves the same web
  frontend as the cloud does; that works with zero extra effort.

## Current State (research summary)

**Identifiers** (`src/dotnet/Api/Identifiers/`, base in
`src/dotnet/Core/Identifiers/StringIdentifier.cs`):
- All IDs are flat strings over `Alphabet.AlphaNumeric` (A–Z a–z 0–9 only).
  Separators in composite IDs: `-` (ChatId kind prefixes `p-`/`s-`, thread
  suffixes, PeerChatId user pair), `:` (AuthorId/ChatEntryId local id), `~`
  (guest UserId prefix, StreamId language suffix). `.` is unused everywhere.
- No tenant/instance/host component exists in any ID. The only "server id" is
  `NodeRef` (cluster node, embedded in transient `StreamId`s).
- `ShardKey`/`ShardKeyResolvers` hash ID string values; the mesh
  (`MeshNode`/`ShardMap`/`MeshRpcRoute`) partitions one logical cluster — it is
  a scaling mechanism, not an isolation boundary.

**Client** (`src/dotnet/Api.Contracts/Module/ApiContractsModule.cs`,
`src/dotnet/Maui/MauiSettings.cs`):
- The Fusion RPC client is architecturally single-server: connection URI
  resolvers throw if `peer.Ref != RpcRef.Default`.
- A server switcher already exists (`MauiPreferences.HostOverride`,
  `MauiAppServerInstanceSelector`, `AppServerInstance`) — but it is
  sequential: switching clears the stored session and reloads the UI.
- Sessions: one `Session` per app install, held by `TrueSessionResolver`,
  injected as a WebSocket/HTTP header. `MauiSession` stores it in
  SecureStorage and creates/validates it via `IMobileSessions`.
- Accounts already support multiple linked identities
  (`AccountFull.Identities`, `DbAccountIdentity`, auto-linking by email in
  `AuthHelper.BuildSignInCommand`).

**Server / self-hosting readiness**:
- Single-binary mode exists: `HostRole.OneServer` runs every backend role in
  one process; role split is config (`HostSettings.ServerRole`).
- Local fallbacks already exist: `LocalFolderBlobStorages` (default when no
  GCS bucket), in-memory queues (`UseNatsQueues=false`),
  `LocalVideoUploadProcessor`, `LogOnlyTextMessageSender`.
- Cloud-only today: transcription (`GoogleTranscriber`/`DeepgramTranscriber`;
  only `FakeTranscriber` is local), push (Firebase via our project), LLM
  features (`ChatSettings.OpenAIKey` etc. — all individually disableable),
  email (needs customer SMTP — dev already uses smtp4dev).
- No OIDC *server* stack (only `Microsoft.IdentityModel.Protocols.OpenIdConnect`
  for validating external tokens).

## Decisions Made

Four forks were evaluated with the team; the chosen options are marked.

### D1. ID scoping across instances → **global instance-id-in-ID**

| Option | Verdict |
|---|---|
| Client-side scoping only (server IDs unchanged, client qualifies at UI edges) | Rejected — every UI surface that touches cross-instance data (cache, links, notifications, navigation) grows ad-hoc composite keys; the ambiguity never goes away. |
| Shared qualified-ID wrapper types (`RemoteChatId = InstanceId + ChatId`) | Rejected — two parallel ID systems forever; every API boundary must decide which one it takes. |
| **Instance segment inside the IDs themselves** | **Chosen** — every ID is globally unique; one ID system; client services and caches work unchanged because IDs simply differ. |

The refactor is far cheaper than it sounds because of one trick: **the cloud
instance is the empty prefix**. Cloud IDs remain byte-identical to today —
no data migration, no wire change, old clients unaffected. On-prem instances
are new installs, so they bake their prefix into every generated ID from day
one — no migration there either. The "global refactor" reduces to teaching
parsers/generators about an optional prefix.

### D2. On-prem user identity → **customer IdP first; "Sign in with Voxt" links, never grants access**

| Option | Verdict |
|---|---|
| **Pluggable instance auth (customer's own IdP as the primary path) + optional Voxt identity linking** | **Chosen** — on-prem accounts are real local accounts (sovereign), authenticated however the customer chooses: their own OIDC IdP (Entra ID, Okta, Keycloak, Google Workspace, …) or local email auth. Most companies will run their own IdP — that is the expected default. "Sign in with Voxt" is an additional, optional schema whose job is **identification and linking** (`voxt/{cloudUserId}` identity), yielding a cross-instance notion of "same person". |
| Cloud as the *primary/only* identity provider for instances | Rejected — enterprises already have IdPs and won't outsource workforce identity to us; it also couples every sign-in to voxt.ai availability. |
| Local accounts only | Rejected as the *only* mode — no cross-instance notion of "same person". Retained as a fallback mode (air-gapped). |
| Users live in cloud; instance hosts places only | Rejected — requires cloud connectivity for every auth decision, leaks membership metadata, weakest sovereignty. |

Two principles worth stating explicitly:

- **Identification is not authorization.** Signing in with a Voxt identity (or
  any identity) on an instance never by itself creates access. The instance
  decides who gets an account and what it can see — via its IdP's directory,
  invite/approval, or admin provisioning.
- **No automatic fan-out sign-in.** Holding a cloud account does not sign the
  user into any instance. Each instance connection is an explicit act, and
  each instance authenticates the user by *its* configured means. The Voxt
  identity merely lets the instance (and the app) recognize that its local
  account and the cloud account are the same person.

Note the cloud learns only "cloud user X linked identity on instance Y" — and
only when the instance enables the `voxt` schema at all.

### D3. Push notifications → **cloud push relay with minimal payload**

| Option | Verdict |
|---|---|
| **Relay: instance → our push gateway → FCM, minimal/encrypted payload** | **Chosen** — the app registers its FCM device token with each instance; the instance sends `{deviceToken, opaquePayload}` to the gateway signed with its instance key; the gateway forwards to FCM. The cloud sees routing metadata only — not even the user, just a device token and an instance id. Content stays in `opaquePayload`, which the instance can leave minimal ("activity in chat X") or encrypt for the app. |
| No push for on-prem in v1 | Rejected as the end state (kept as the automatic behavior for instances that disable the relay). |
| Relay with full content | Rejected — previews transiting our cloud contradicts the sovereignty pitch. |

### D4. Registration enforcement → **required for keys, soft for connectivity**

| Option | Verdict |
|---|---|
| Required + app-verified (app refuses unregistered instances) | Rejected — hostile to tinkerers and air-gapped evaluation; a hard gate invites forks/workarounds. |
| **Registration issues `InstanceId` + keys; app connects to any URL but shows unregistered ones as "unverified"** | **Chosen** — every *useful* instance registers in practice, because the keys are what unlock the push relay, Sign in with Voxt, discovery, and the "verified" badge. |
| Optional / telemetry-only | Rejected — fails the "we want to know about them" goal. |

## Architecture

### 1. InstanceId and qualified IDs

New type `InstanceId` in `src/dotnet/Api/Identifiers/`:

- Lowercase alphanumeric, 6–12 chars, generated by the cloud registry.
- `InstanceId.None` (empty) = our cloud. Reserved values: `local` (dev/
  unregistered instances self-assign nothing — an unregistered instance runs
  with a self-generated *provisional* id prefixed `x`, e.g. `xk3f9a2`, so its
  data is still globally unique if it registers later; see Registration).

Qualified ID grammar: `{instanceId}.{localId}`, with the dot absent for cloud
IDs. `.` is outside `Alphabet.AlphaNumeric`, unused by any current separator,
and URL-unreserved, so:

- `TryParse` of every ID type splits on the *first* `.`; no dot → cloud,
  exactly today's code path. Cost: one `IndexOf('.')` per parse.
- The prefix applies **once, at the front of the whole ID**; embedded local
  parts stay local. Examples (instance `acme01`):
  - GroupChatId: `acme01.x8Kq2mVb3N`
  - PlaceChatId: `acme01.s-{placeId}-{localChatId}`
  - PeerChatId: `acme01.p-{localUserId1}-{localUserId2}` — both users are by
    definition on the same instance until federation exists.
  - AuthorId: `acme01.{localChatId}:{localId}` (split on `:` first, then the
    chat part parses as a qualified ChatId — or equivalently split the prefix
    first; either order is unambiguous).
  - Guest UserId: `acme01.~a1b2c3d4` — guest check moves from `Value[0]` to
    `LocalValue[0]`.
- `StringIdentifier` gains `InstanceId Instance` and `string LocalValue`
  (both computed at parse time, cached like `HashCode` is today).
- Generators: server holds its `InstanceId` in config
  (`CoreServerSettings.InstanceId`, empty in cloud); every `IdGenerator` call
  site prefixes through one shared helper, so cloud codegen output is
  unchanged.
- `ShardKey` resolvers hash the full string — no change needed.
- DB: on-prem databases store fully qualified IDs. Cloud DBs are untouched.
- Validation hardening: an instance's API **rejects commands referencing IDs
  of a different instance** (server knows its own id) — this is the tenancy
  guard that today doesn't exist and costs one prefix comparison.

Cross-instance ID references (mentions, invites, deep links) become
representable for free — a qualified ID is self-describing. Resolving one to
an endpoint goes through discovery (below).

### 2. Instance registry, keys, discovery

New cloud-side service (suggested: `Instances.Service` + `IInstances` /
`IInstancesBackend` contracts, standard module layout):

- **Register**: admin signs in at voxt.ai, creates an instance → gets
  `InstanceId`, an Ed25519 key pair (private key shown once / downloadable,
  public key stored by us), and a signed **instance descriptor**: JWS over
  `{instanceId, publicKey, displayName, endpointUrl?, issuedAt}` signed by our
  root key (root public key pinned in app builds).
- The instance serves its descriptor + a fresh self-signed proof at a
  well-known endpoint, e.g. `GET /.well-known/voxt-instance` →
  `{descriptor, proof}` where `proof` is a signature over a client-supplied
  nonce with the instance private key. Apps verify: descriptor is
  root-signed, proof matches descriptor's public key → **verified badge**.
  No descriptor/invalid proof → connect anyway, marked **unverified** (D4).
- **Discovery**: optional `instanceId → endpointUrl` lookup on the registry,
  used to resolve qualified IDs in deep links (`voxt.ai/i/{instanceId}/...`).
  Instances that don't publish an endpoint are reachable only via direct
  URL / QR.
- **Connect UX**: instance admin generates a QR / invite link that encodes
  `{endpointUrl, instanceId}`; scanning it in the app adds the instance and
  starts sign-in.
- Registration is also the **licensing hook** — the registry knows every
  registered installation, its descriptor issuance/renewal is our contact
  point (e.g. yearly renewal), without any access to instance data.

### 3. Multi-connection client

The choice in D1 makes this the clean part: because IDs are globally unique,
**client services, caches, and UI state stay singular** — only the transport
fans out.

- **Routing**: replace the `RpcRef.Default` guard with a client-side
  `RpcCallRouter` that maps a call's target instance → the matching
  `RpcPeerRef` (one WebSocket per connected instance). The server mesh already
  routes calls by argument (`MeshRpcRef`/`MeshRpcRoute`); this mirrors that
  pattern client-side. Target instance = the `Instance` of the call's first
  ID-bearing argument; session-only calls route by an explicit instance
  context.
- **Sessions**: `TrueSessionResolver` (single session) is extended/wrapped by
  a per-instance session map (`InstanceId → Session`), each stored in
  SecureStorage as today (`MauiSession` becomes per-instance). Each peer's
  WebSocket carries its own session header.
- **UrlMapper**: becomes per-instance (`UrlMapper.For(instanceId)`), so media,
  image-proxy, and content URLs point at the owning instance. On-prem
  instances default to same-host paths instead of `cdn.`/`media.` subdomains
  (config).
- **Merged UI**: chat list, contacts, and search aggregate across instances.
  Fusion makes this natural — each instance's data arrives via its own
  computed state; the list-level services combine them. Per-instance
  connectivity indicators (an instance can be offline while cloud is up —
  e.g. off-VPN) reuse `ConnectivityUI` per peer.
- **`AppServerInstance` / `HostOverride`** stays as the "connect the app to a
  different *primary* server" mechanism (dev/testing); connected on-prem
  instances become a *list* stored next to it.
- **RemoteComputedCache**: keys embed args; qualified IDs differ per instance,
  so cached entries never collide. Cache eviction on instance disconnect =
  optional hygiene, not correctness.

### 4. Instance authentication: BYO IdP + Voxt identity linking

Instance auth is pluggable, in order of expected prevalence:

- **Customer IdP (the expected default)**: the instance configures a generic
  OIDC relying party against the customer's own provider — Entra ID, Okta,
  Keycloak, Google Workspace, or anything OIDC-compliant — via the standard
  ASP.NET `AddOpenIdConnect` handler (the
  `Microsoft.IdentityModel.Protocols.OpenIdConnect` package is already
  referenced). `AuthSchema` gains a generic `oidc` schema; the existing
  `AuthHelper.SignIn` flow creates/links the local account from the IdP
  claims exactly as Google/Apple sign-in does today, reusing
  `DbAccountIdentity` and email-based auto-linking unchanged. Authorization
  (who may have an account at all) stays with the instance: IdP directory
  membership, invite/approval, or admin provisioning.
- **Local email auth**: their SMTP, for air-gapped or IdP-less deployments.
- **Sign in with Voxt (optional, additive)**: cloud adds an OIDC
  authorization server (recommendation: **OpenIddict**, authorization-code +
  PKCE only). Each registered instance is a pre-registered OIDC client
  (client id = `InstanceId`, keys from registration). On the instance this is
  just one more schema (`voxt`), producing identity `voxt/{cloudUserId}` on
  the local account.

What "Sign in with Voxt" is for — and what it is not:

- It **identifies and links**: the instance account carries the person's
  cloud identity, so the instance can invite/recognize people by their Voxt
  identity and the app can group accounts belonging to the same person.
- It is **not** a skeleton key: it never signs the user into other instances,
  and it never creates access on an instance that hasn't provisioned or
  approved that account. Per-instance sign-in remains an explicit act against
  that instance's configured auth.
- The app streamlines rather than automates: when adding an instance, it can
  pre-drive the relevant flow (e.g. the `MauiAuthController` token-cookie
  pattern for the `voxt` schema, or the system browser for the customer IdP),
  but the sign-in and any approval step are per instance, every time.
- Linking also works *after* the fact: a user signed in via the customer IdP
  can attach their Voxt identity to the same local account later (standard
  add-identity flow) — or never; the app then just treats the sessions as
  unrelated accounts on one device.

### 5. Push relay

- App registers its FCM token with every connected instance (existing device
  registration flow, per instance).
- Instance-side: a new `IPushRelayClient` behind the existing notification
  abstraction — where cloud uses `FirebaseMessagingClient` directly, on-prem
  substitutes the relay client (selection by config, same pattern as
  blob-storage selection in `CoreServerModule`).
- Cloud-side: `PushRelay` endpoint accepting
  `{deviceTokens[], payload, badge?}` signed (JWS) with the instance key;
  validates against the registry, rate-limits per instance, forwards via FCM.
- Payload policy is instance-configurable: `Minimal` (default —
  `{instanceId, chatId, kind}`; the app wakes, fetches content from the
  instance, renders a local notification) or `None` (relay disabled).
  A later `Encrypted` mode can carry instance-encrypted previews once a
  device-key scheme exists (dovetails with the E2EE plan).
- iOS caveat: silent pushes are throttled; `Minimal` should use a Notification
  Service Extension that fetches the preview from the instance within the
  30-second window (the iOS share/notification extensions already share
  session storage via `AppleSharedSecureStorage`).

### 6. On-prem configuration and distribution

- **Distribution**: a `docker-compose.onprem.yml` bundle — app server
  (existing `Dockerfile` `app` stage), migrations container (existing
  `migrations-app` stage), postgres, redis/valkey, nats, optional opensearch,
  optional embeddings service. Config via `.env` + mounted
  `appsettings.local.json` (the layering in `AppHost.Build.cs` already
  supports exactly this). `ServerRole=OneServer`.
- **Config profile** (all existing settings, documented as the "on-prem
  matrix"):
  - Storage: `GoogleStorageBucket` empty → `LocalFolderBlobStorages`
    (mounted volume). S3-compatible `IBlobStorage` is a candidate follow-up.
  - Transcription: bring-your-own `DeepgramKey` or Google credentials
    (data goes to *their* vendor account, not us), or transcription off.
    A self-hosted Whisper-based `ITranscriber` implementation is the real
    sovereignty answer — new implementation behind the existing
    `ITranscriberFactory`, sized as its own follow-up plan.
  - LLM features: their own `OpenAIKey`/model or per-feature disable flags
    (all exist: `IsTranslationEnabled`, `IsSummarizationEnabled`,
    `IsRetranscriptionEnabled`, `MLSearchSettings.IsEnabled`, …).
  - Email: their SMTP (`UsersSettings.Smtp*`). SMS: off
    (`LogOnlyTextMessageSender`) — phone auth is not offered on-prem.
  - Push: relay on/off + payload policy.
  - Auth: `voxt` (OIDC against cloud) and/or `email`; Google/Apple OAuth are
    possible only if the customer brings their own OAuth apps (documented,
    not recommended).
- **Data-egress guarantee** (documented and testable): with relay and OIDC
  off, the instance makes zero calls to voxt.ai; with them on, the only
  calls are descriptor renewal, OIDC token exchange, and push relay posts.
  An integration test asserts the outbound allowlist.

### 7. Version compatibility (apps move fast, instances move slow)

Our apps ship weekly; on-prem servers will lag. Untreated, RPC/contract drift
would break connections silently. Plan:

- The instance descriptor endpoint reports server version + supported API
  contract range; the app checks it at connect time and shows
  "instance needs an update" / "app too old for this instance" states instead
  of failing opaquely.
- MemoryPack `VersionTolerant` + additive-only contract evolution is the
  compatibility policy for all `IApi*` contracts from the first on-prem
  release; breaking changes require a contract-version bump gated by the
  handshake.
- The compose bundle ships with optional auto-update (watchtower-style,
  customer's choice) to keep the fleet close to current.

## Reuse

### Existing abstractions to reuse

- `StringIdentifier` / `IStringIdentifier<T>` + per-type LRU parse caches —
  extended, not replaced, for qualified IDs
  (`src/dotnet/Core/Identifiers/StringIdentifier.cs`).
- `RandomStringGenerator` + per-type `IdGenerator` statics — ID generation,
  gains the instance-prefix helper.
- `ShardKeyResolvers` / `IHasShardKey` — unchanged; hashes the full value.
- `RpcCallRouter` pattern from ActualLab.Rpc (as used by `MeshRpcRef` /
  `MeshRpcRoute` in `src/dotnet/Core.Server/Rpc/`) — client-side
  instance→peer routing mirrors it.
- `TrueSessionResolver`, `MauiSession`, `AppleSharedSecureStorage` — session
  storage, generalized per instance.
- `AppServerInstance`, `MauiPreferences.HostOverride`,
  `MauiAppServerInstanceSelector` — basis of the instance list + switcher UX.
- `UrlMapper` — per-instance instantiation.
- `UserIdentity` / `DbAccountIdentity` / `AuthHelper` sign-in & auto-linking —
  the `voxt` OIDC identity plugs in like Google/Apple do.
- `AuthSchema` — new `oidc` (generic customer IdP) and `voxt` schema
  constants.
- `MauiAuthController` token-cookie flow — reused for the automated OIDC hop.
- `ISecureTokens` — nonces/handshake tokens where needed.
- `IBlobStorages` selection pattern in `CoreServerModule` — template for
  push-provider selection; `LocalFolderBlobStorages`,
  `LocalVideoUploadProcessor`, `LogOnlyTextMessageSender`,
  in-memory queues — the on-prem fallbacks, already in place.
- `ITranscriberFactory` / `ITranscriber` — extension point for BYO-key and
  future Whisper transcriber.
- `FirebaseMessagingClient` / `IFirebaseMessagingClient` — the cloud side of
  the relay forwards through it.
- `HostRole.OneServer`, `HostSettings`, config layering in
  `AppHost.Build.cs`, `Dockerfile` `app`/`migrations-app` stages — the
  self-host bundle is packaging, not new hosting code.
- `Features` / `FeatureDef`, `IServerSettings` (KVAS) — instance-side feature
  toggles surfaced to clients.
- `ConnectivityUI` / `AppRpcClientPeerReconnectDelayer` — per-peer
  connectivity UX.

Nothing was found that already models tenancy/instance identity — `InstanceId`
and the registry are genuinely new.

### Reusability of new components

| New component | Local option | Shared option | Recommendation |
|---|---|---|---|
| `InstanceId`, qualified-ID parsing | — | `src/dotnet/Api/Identifiers/` + `Core/Identifiers/` | **Shared by necessity** — every ID type builds on it. |
| Instance descriptor + JWS sign/verify | Instances.Service only | `ActualChat.Core.Server` (`Security/`) | **Shared** — verification is needed by apps (client!) and relay; put the descriptor model + verify in `Api`/`Core` (client-reachable), signing in `Core.Server`. |
| `IInstances` registry contracts | — | `Api.Contracts` like other frontend contracts | **Shared** (standard contract placement). |
| Push relay contract (`IPushRelay`) + `IPushSender` abstraction over FCM/relay | Notifications.Service | abstraction in `ActualChat.Core.Server`, impls in Notifications.Service | **Shared abstraction** — mirrors `IBlobStorages`; the FCM-vs-relay switch is exactly the storage-selection pattern. |
| Client instance-connection manager (peer map, session map, router) | UI.Blazor.App | `Api.Contracts/Module` (next to `ApiContractsModule`/`RpcSwitchingClient`) | **Shared** — it is transport plumbing, not UI; UI.App layers the UX on top. |
| OIDC server (OpenIddict wiring) | Users.Service module | — | **Local to Users.Service** — cloud-only concern, fits the existing auth-provider wiring there. |
| Whisper `ITranscriber` (follow-up) | Streaming.Service transcribers folder | — | **Local** — sibling of Google/Deepgram implementations. |

## Phasing

Each phase ships value on its own; later phases don't block earlier ones from
being useful.

1. **Qualified-ID plumbing** — `InstanceId`, parser/generator support across
   all ID types, instance-mismatch command guard, exhaustive parse/round-trip
   tests. Invisible to users; cloud behavior byte-identical. This is the
   riskiest code change, so it lands first and soaks.
2. **Self-host bundle** — compose file, on-prem config profile + docs,
   outbound-allowlist test. At this point an instance is usable via its web
   client and via the app's existing `HostOverride` (single-server mode) —
   a real early-adopter milestone.
3. **Registry + verification** — Instances.Service, registration UX on
   voxt.ai, descriptors/keys, `.well-known` endpoint, app-side verify +
   QR/invite connect flow (still single-server switching).
4. **Instance auth** — generic `oidc` schema for customer IdPs (the main
   deliverable), OpenIddict on cloud + `voxt` schema for identity linking,
   app-streamlined per-instance sign-in.
5. **Multi-connection client** — the router, per-instance sessions and
   UrlMapper, merged chat list/contacts/search, per-instance connectivity UX.
   The largest phase; ends the era of switching.
6. **Push relay** — instance client, cloud gateway, payload policies, iOS
   notification-extension fetch path.
7. **Follow-ups** — Whisper transcriber, S3 blob storage, encrypted push
   payloads, discovery-based deep links, cross-instance invites.

## Risks

- **ID parsing regressions (Phase 1)** — the parsers are hot and central;
  mitigations: change is additive (`IndexOf('.')` fast path when absent),
  exhaustive round-trip tests over all ID types, fuzzing the parsers, long
  soak on dev before any on-prem release.
- **Contract drift between fast-moving apps and slow-moving instances** —
  addressed in §7 (handshake + version-tolerant policy); the residual risk is
  discipline, so CI should diff-check API contracts for breaking changes.
- **iOS push UX** — extension-based fetch has tight time budgets and fails
  when the instance is unreachable (off-VPN); the fallback is a contentless
  "new activity" notification, which must be acceptable.
- **OIDC availability coupling** — if voxt.ai is down, `voxt`-schema
  sign-ins fail (existing sessions keep working; customer-IdP and email
  sign-ins are unaffected — one more reason BYO IdP is the primary path).
  Mitigation: long instance session lifetimes (90-day default already).
- **Security surface** — instance private keys, relay abuse (rate limiting,
  token audits), and the app now connecting to arbitrary servers (the RPC
  client must treat instance servers as untrusted: no cloud session/tokens
  ever sent to an instance peer — per-instance sessions guarantee this by
  construction, but tests should assert it).
- **Support burden** — arbitrary customer environments; mitigations: the
  compose bundle is the only supported topology at first, plus a built-in
  diagnostics page (self-check of config, connectivity, versions).

## Open Questions

1. **Licensing/pricing model** — registration is the hook, but what does a
   registered instance cost, and does the descriptor expire (renewal =
   enforcement point)?
2. **Cross-instance peer chats** — a cloud user DM'ing an on-prem user
   requires federation-grade delivery; qualified IDs make it representable,
   but is it ever in scope?
3. **MLSearch/OpenSearch on-prem** — optional (heavy) component; ship in the
   bundle as opt-in, or defer entirely?
4. **Instance-hosted media in previews** — link previews and mention
   resolution for cross-instance references need per-instance fetch paths in
   the app; scope for Phase 5 or 7?
5. **How many instances per app** — UX and resource ceiling (each is a live
   WebSocket + state); propose a soft cap (e.g. 5) initially?
6. **Provisional (`x`-prefixed) instance ids** — keep data portable into a
   registered id later, or require registration before first real use to
   avoid a rename migration?
7. **SAML** — some enterprises are SAML-only; is generic OIDC enough for v1,
   or do we need SAML (directly or by documenting an IdP-side
   SAML-to-OIDC bridge, e.g. Keycloak/Dex)?
