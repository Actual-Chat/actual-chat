# OAuth 2.1 authorization server for MCP clients — design

**Status:** approved design, 2026-09-15. Working document — delete once shipped;
the living doc is `docs/oauth.md`.

## Goal

Let any MCP client — Claude (claude.ai, Cowork, Desktop, Claude Code), Cursor,
VS Code, custom agents — connect to Voxt's MCP endpoint (`/api/mcp`) through a
standard OAuth "Connect" flow, acting as the user who consented, with per-user
revocation from Settings. Today the endpoint accepts only a pasted API key,
which Claude's hosted surfaces cannot send per user.

## Decisions (from the brainstorm)

| # | Decision |
|---|---|
| 1 | Support **any** MCP client: Dynamic Client Registration (RFC 7591) **and** Client ID Metadata Documents (CIMD). |
| 2 | **Separate tokens, shared principal.** API keys stay what they are. OAuth clients get JWT access tokens + rotated refresh tokens; each user consent is backed by exactly one `SessionKind.OAuth` session that the tools run under. |
| 3 | **OpenIddict** (server + validation + EF stores) in its **own DbContext**; DCR, CIMD and the RFC 9728 document are hand-written on top. |
| 4 | OAuth lives in **new `OAuth.Contracts` / `OAuth.Service` / `OAuth.Service.Migration`** projects — MCP is only the first resource server. |
| 5 | Consent is a **Blazor page** in the app (`/oauth/consent`), reusing the existing sign-in machinery. |
| 6 | One scope `mcp` (+ `offline_access`); code 5 min, access token 1 h, refresh token 90 d rotated, backing session 90 d sliding. |
| 7 | No new settings tab: the API-keys tab becomes **"API & apps"** with an *API keys* section and a *Connected apps* section. |
| 8 | Proof: raw-HTTP protocol tests + MCP C# SDK OAuth-client end-to-end test + manual checklist (Claude Code vs `local.voxt.ai`, Cowork vs dev). No Playwright. |

## Architecture

### Projects and components

| Piece | Project | Role |
|---|---|---|
| `OAuthModule`, `OAuthSettings` | `OAuth.Service` | Registers OpenIddict server/validation/EF, endpoints, pruner. Active only on `HostRole.Api` when `OAuthSettings.Route` is set (same gate as `McpModule`). |
| `OAuthDbContext`, `OAuthDbInitializer` | `OAuth.Service/Db` | `UseOpenIddict()` tables + Fusion `operations`/`events`; layout copied from `InviteDbContext`. DB name follows the `{context}` convention (`oauth`). |
| `OAuth.Service.Migration` | new project | EF migrations, like `Invite.Service.Migration`. |
| `ClientRegistrationEndpoint` | `OAuth.Service` | `POST /oauth/register` (DCR). |
| `CimdClientResolver` | `OAuth.Service` | OpenIddict handler: `client_id` that is an `https://` URL → fetch metadata → JIT application. |
| `LoopbackRedirectUriValidator` | `OAuth.Service` | OpenIddict handler: port-agnostic loopback redirect match (RFC 8252). |
| `AuthorizeEndpoint`, `TokenEndpoint` | `OAuth.Service` | Pass-through handlers for OpenIddict's authorize/token endpoints. |
| `ServerMetadataExtender` | `OAuth.Service` | Adds DCR/CIMD/`none` entries to the RFC 8414 document. |
| `IOAuthGrants` / `OAuthGrants` | `OAuth.Contracts` / `OAuth.Service` | `List`, `GetClient`, `Approve`, `Revoke`. Owns the backing session (via `ISessionsBackend` / `IAccountsBackend` over RPC). |
| `OAuthBearerAuthenticator` | `OAuth.Service` | `TryGetSession(HttpContext)`: JWT → `sid` → active `Session`. Reusable by any future resource endpoint. |
| `OAuthPruner` | `OAuth.Service` | Hourly `WorkerBase`: orphan DCR apps, expired tokens, authorizations whose session died. |
| `SessionKind.OAuth` + id prefix | `Core` (`SessionKind.cs`, `SessionExt.cs`, `CoreConstants.cs`) | Distinguishes grant sessions from API keys and browser sessions. |
| `ProtectedResourceEndpoint` | `Mcp` | `GET /.well-known/oauth-protected-resource[/api/mcp]` — the resource names itself. |
| `McpAuthMiddleware` (existing) | `Mcp` | API-key path unchanged; non-API-key bearer → `OAuthBearerAuthenticator`; 401 shape gains `resource_metadata` + `scope`. |
| `OAuthConsentPage.razor` | `UI.Blazor.App/Pages` | Consent screen. |
| `ConnectedAppsSettings.razor` | `UI.Blazor.App/Components/Settings` | Grants list + revoke; composed into the renamed API-keys tab. |

### Reuse

Existing abstractions used as-is: `HostModule<TSettings>` + `IServerModule`,
`DbModule.AddDbContextServices`, `DbContextBase`, `DbInitializer<T>`,
`DbServiceBase<T>`, `rpcHost.AddApi/AddBackend`, `ISessionsBackend` /
`SessionsBackend_Upsert`, `IAccountsBackend` / `AccountsBackend_SignOut`,
`Accounts.GetOwn` + `AccountFull.MustNotBeGuest/MustBeActive`, `SessionExt`
prefix-based `Kind`, `UrlMapper.BaseUri`, `AccountUI.RequestSignInFromHomePage`,
`ComputedStateComponent`, `TileTopic`/`Tile`/`TileItem`/`ButtonRound`,
`WorkerBase`, `SharedAppHostTestBase` / `WebClientTester`, `McpClient`
(`ModelContextProtocol` 1.3.0, already referenced — its `ClientOAuthOptions`
drives the end-to-end test).

New shared component: `OAuthBearerAuthenticator` is deliberately placed in
`OAuth.Service` (not `Mcp`) so a second resource server reuses it.

### Request flows

**First connection, DCR client (Cursor, VS Code):**

1. `POST /api/mcp` without a token → `401`,
   `WWW-Authenticate: Bearer resource_metadata="<base>/.well-known/oauth-protected-resource/api/mcp", scope="mcp"`.
2. Client reads the RFC 9728 document → `authorization_servers: ["<base>"]` →
   reads `<base>/.well-known/oauth-authorization-server`.
3. `POST /oauth/register` → `client_id` (public client, PKCE required).
4. Browser → `GET /oauth/authorize?…`. The handler reads the Fusion session
   cookie. Guest, **or** no permanent authorization for (client, user, scopes)
   → `302 /oauth/consent?<same query>`. The consent page signs the user in if
   needed, shows client + scopes; **Approve** → `OAuthGrants_Approve` (creates
   the OpenIddict permanent authorization and the backing session) → full
   navigation back to `/oauth/authorize?<same query>` → authorization found →
   OpenIddict issues the code → redirect to the client with `code` + `state`.
5. `POST /oauth/token` (`authorization_code` + `code_verifier`) → JWT access
   token + refresh token.
6. `POST /api/mcp` with the JWT → middleware → `OAuthBearerAuthenticator` →
   `SessionsBackend.Get(sid)` active → tools run as that user.

**CIMD client (Claude surfaces, Claude Code):** identical minus step 3;
`client_id=https://claude.ai/oauth/…` is resolved at step 4 by
`CimdClientResolver`.

**Refresh:** `POST /oauth/token grant_type=refresh_token` → OpenIddict
validates and rotates → we upsert the backing session (`LastSeenAt = now`,
`ExpiresAt = now + 90 d`).

**Revoke (user, Connected apps):** `OAuthGrants_Revoke` → OpenIddict
`TryRevokeAsync` on the authorization (cascades to its tokens) +
`AccountsBackend_SignOut(session, Deactivate: true)`. Live JWTs are refused on
the next request because the session check fails.

**Deny:** consent page navigates to `/oauth/authorize?<query>&voxt_deny=1`;
the handler answers `error=access_denied` (+ `state`) to the client's
`redirect_uri`.

## Data model & lifecycle

### OpenIddict store (`OAuthDbContext`, own DB)

- `applications` — one per client. `client_type = public`,
  `consent_type = explicit`, permissions: grant types
  `authorization_code`, `refresh_token`; endpoints `authorization`, `token`,
  `revocation`; scopes `mcp`, `offline_access`; `redirect_uris` from DCR/CIMD.
  `client_id`: random for DCR apps, the metadata URL itself for CIMD apps.
  Properties: `registered_via` (`dcr` | `cimd`), `cimd_fetched_at`.
- `authorizations` — one **permanent** row per (application, user, scopes) =
  the consent. Properties: `sid` (backing session id).
- `tokens` — authorization codes and refresh tokens as reference tokens
  (OpenIddict tracks redeemed/revoked). Access tokens are JWTs, not stored.
- `scopes` — seeded on init: `mcp` ("Read and post in your chats via MCP").
  `offline_access` is built in.
- Fusion `operations` / `events` so `DbModule.AddDbContextServices` and
  `DbServiceBase<OAuthDbContext>` work. OpenIddict entities are touched only
  through its managers (plain EF, outside Fusion's operation log — nothing
  reactive depends on them); grant commands run under `DbServiceBase` so
  `IOAuthGrants.List` invalidates.

### Backing session (`_Sessions`, Users DB)

- Id prefix `@` → `SessionKind.OAuth` (`SessionExt.NewOAuth()`,
  `CoreConstants.Session.OAuthPrefix`).
- Created by `OAuthGrants_Approve` via `SessionsBackend_Upsert { UserId,
  Description = "<client name> (OAuth)", ExpiresAt = now + 90 d }`;
  `Options["authorization_id"]` links back.
- Never shown as a device or an API key: `Accounts.ListSessions` consumers
  (sessions tab, API-keys list) filter on `Kind`; the grant is shown instead,
  in Connected apps.
- `AuthHelper.SignIn` / `UpdateAuthState` reject it exactly like `ApiKey`.
- `Accounts_DeactivateSession` still works on it (break-glass revoke).

### JWT access token

Signed, not encrypted (`DisableAccessTokenEncryption`) so ops can read it.
Lifetime 1 h. Claims: `sub` = user id, `client_id`, `scope`, `aud` = MCP
resource URL (or the `resource` the client asked for), `sid`, OpenIddict's
authorization id. Signing key: dev → `AddDevelopmentSigningCertificate`;
prod → certificate from `OAuthSettings.SigningCertificate` (secret). Key
rotation: add the new key, keep the old one until its last token expired.

### Lifecycle rules

| Event | Effect |
|---|---|
| Approve | authorization + backing session created (idempotent per client/user/scopes) |
| Code exchange / refresh | tokens bound to the authorization; session `LastSeenAt`/`ExpiresAt` bumped |
| User revoke | authorization + tokens revoked; session deactivated |
| `/oauth/revoke` by client | token revoked; if it was the authorization's last live refresh token, session deactivated |
| Pruner (hourly, Api role) | DCR apps with no authorization and `created_at < now − 24 h` deleted; expired/redeemed tokens deleted; authorizations whose session is inactive revoked |
| Account deleted | Users side removes sessions; pruner revokes the orphaned authorizations |

## Endpoints & OpenIddict configuration

Issuer = `UrlMapper.BaseUri`. `OAuthSettings.Route = "/oauth"`.

| Route | Owner | Notes |
|---|---|---|
| `GET /.well-known/oauth-authorization-server` | OpenIddict + `ServerMetadataExtender` | adds `registration_endpoint`, `client_id_metadata_document_supported: true`, ensures `token_endpoint_auth_methods_supported` ∋ `none`, `code_challenge_methods_supported: ["S256"]`, `scopes_supported: ["mcp","offline_access"]` |
| `GET /.well-known/openid-configuration` | OpenIddict | same document; some clients probe it first |
| `GET /.well-known/oauth-protected-resource[/api/mcp]` | `Mcp` | `{ resource: "<base>/api/mcp", authorization_servers: ["<base>"], scopes_supported: ["mcp"], bearer_methods_supported: ["header"] }` |
| `POST /oauth/register` | `OAuth.Service` | RFC 7591. Validates `redirect_uris` (https, or http loopback; no fragments), `grant_types ⊆ {authorization_code, refresh_token}`, `token_endpoint_auth_method = none` (anything else → `invalid_client_metadata`). Returns `client_id`, `client_id_issued_at`, echoed metadata. No RFC 7592 management. Per-IP rate limit (20/min). |
| `GET\|POST /oauth/authorize` | OpenIddict + `AuthorizeEndpoint` | flow above; `SignIn(principal)` with `SetAuthorizationId` so the code is bound to the permanent authorization |
| `POST /oauth/token` | OpenIddict + `TokenEndpoint` | `authorization_code`: build principal (claims above; `sid` from the authorization's properties); destinations: all claims → access token, `sub`+`sid` → refresh token. `refresh_token`: OpenIddict validates/rotates; we bump the session. |
| `POST /oauth/revoke` | OpenIddict + handler | RFC 7009; deactivates the session when the authorization has no live refresh token left |

**Server options:** `AllowAuthorizationCodeFlow().AllowRefreshTokenFlow()`,
`RequireProofKeyForCodeExchange()`, `RegisterScopes("mcp", "offline_access")`,
`SetAuthorizationCodeLifetime(5 min)`, `SetAccessTokenLifetime(1 h)`,
`SetRefreshTokenLifetime(90 d)`, `DisableAccessTokenEncryption()`,
`UseReferenceRefreshTokens()`, `SetIssuer(base)`,
`UseAspNetCore().EnableAuthorizationEndpointPassthrough().EnableTokenEndpointPassthrough()`.
Resource indicators (RFC 8707): accepted and reflected as `aud`; default
`aud` = MCP resource URL.

**Validation:** `AddValidation().UseLocalServer().UseAspNetCore()`.
`OAuthBearerAuthenticator.TryGetSession` authenticates with the validation
scheme, requires `aud` ∋ MCP resource URL, reads `sid`, calls
`ISessionsBackend.Get`, requires active + non-guest user.

**`McpAuthMiddleware`:** bearer starts with the API-key prefix → existing
path; otherwise → `OAuthBearerAuthenticator`; any failure → the 401 shape
above (`error="invalid_token"` when a token was present).

**CIMD handler:** `IOpenIddictServerHandler<ValidateAuthorizationRequestContext>`
ordered before OpenIddict's `ValidateClientId`. If `client_id` is an absolute
`https://` URL and the app is missing or `cimd_fetched_at` is older than
`OAuthSettings.CimdCacheAge` (24 h): fetch (5 s timeout, 64 KB cap,
`application/json`, no redirects); require the document's `client_id` to
equal the URL; take `client_name`, `redirect_uris`; create or update the app.
Any failure → `invalid_client`.

**Loopback redirects:** custom `ValidateRedirectUri` handler — when the
registered URI is `http://localhost`, `http://127.0.0.1` or `http://[::1]`,
the request's URI matches with any port (RFC 8252 §7.3). Everything else is
exact match (OpenIddict default).

## Consent UI & grants API

### `IOAuthGrants` (`OAuth.Contracts`)

```
[ComputeMethod] Task<ApiArray<OAuthGrant>> List(Session, CancellationToken)
[ComputeMethod] Task<OAuthClientInfo?> GetClient(Session, string clientId, CancellationToken)
[CommandHandler] Task<string> OnApprove(OAuthGrants_Approve { Session, ClientId, Scopes }, CancellationToken)
[CommandHandler] Task OnRevoke(OAuthGrants_Revoke { Session, AuthorizationId }, CancellationToken)

OAuthGrant(Id, ClientId, ClientName, Scopes, CreatedAt, LastUsedAt, ExpiresAt)
OAuthClientInfo(ClientId, ClientName, RedirectHosts, IsLoopback, Scopes)
```

Registered with `rpcHost.AddApi` (session-scoped, client-callable).
`Approve` requires a non-guest, active account (same `Require`s as
`Accounts.OnCreateApiKey`), is idempotent per (client, user, scopes), and
returns the authorization id. Both commands invalidate `List` for the user.

### Consent page (`/oauth/consent`)

- Keeps the authorize query verbatim for the round-trip.
- Guest → `AccountUI.RequestSignInFromHomePage(L.SignIn_ToAuthorizeApp,
  History.LocalUrl)`; returns here after sign-in (the `ChatInvitePage`
  pattern).
- Shows: client name, redirect host(s) (impostor check; loopback clients get a
  "This app runs on your computer" line), scopes in plain words ("Read and
  post in your chats", "Stay connected without asking again"), the account
  being linked.
- **Approve** → `OAuthGrants_Approve` → full navigation to
  `/oauth/authorize?<query>`.
- **Deny** → full navigation to `/oauth/authorize?<query>&voxt_deny=1`.
- Unknown client / malformed query / OpenIddict error pass-through
  (`error`, `error_description` in the query) → error tile, no redirect.
- Standalone page (Claude opens it in a new tab), app header, mobile widths.

### Settings

`SettingsTabId.ApiKeys` stays; label becomes "API & apps"
(`L.Settings_ApiAndApps`). The tab renders `ApiKeySettings` (unchanged) and,
below it, `ConnectedAppsSettings`: one tile item per `OAuthGrant` — client
name, scope summary, "Connected <date> · Last used <date>", trash button →
confirm → `OAuthGrants_Revoke`. Empty state: "Apps you authorize via OAuth
(Claude, Cursor, …) appear here." No create action. Both are separate
`ComputedStateComponent`s.

## Errors, security & ops

**Error shapes**

- MCP: no token → `401` + `WWW-Authenticate: Bearer resource_metadata="…", scope="mcp"`;
  bad/expired token → same with `error="invalid_token"`; wrong scope →
  `403`, `error="insufficient_scope"` (reserved).
- Token endpoint (OpenIddict, RFC 6749): `invalid_grant` for bad/replayed
  code, wrong verifier, rotated-away or revoked refresh token;
  `invalid_client` for unknown/unfetchable client; `invalid_redirect_uri`.
  Client mistakes never produce a 500.
- Authorize: malformed → error rendered on the consent page; denial →
  `access_denied` redirect with `state`.

**Security invariants**

- PKCE S256 mandatory; public clients only; no secrets stored.
- Codes single-use; replay revokes the authorization's tokens (OpenIddict
  default). Refresh tokens rotate; the old one is dead immediately.
- `redirect_uri` exact match except loopback port. DCR refuses non-https
  non-loopback URIs and fragments.
- CIMD: https only, no redirects, size/time capped, `client_id` must equal
  the URL, cached `CimdCacheAge`.
- `aud` enforced per resource.
- Consent shows client name and redirect host.
- Backing sessions are `SessionKind.OAuth`: refused for web sign-in, hidden
  from device lists, revocable.
- `/oauth/register` rate-limited; orphan DCR apps pruned after `DcrPruneAge`.
- Signing certificate from a secret in prod; ephemeral dev cert locally
  (tokens do not survive a dev restart; refresh recovers).

**Settings (`OAuthSettings`)**: `Route`, `SigningCertificate`,
`AccessTokenLifetime`, `RefreshTokenLifetime`, `AuthorizationCodeLifetime`,
`DcrPruneAge`, `CimdCacheAge`, `RegisterRateLimitPerMinute`.

**Observability**: one structured log line per register / consent / token /
refresh / revoke with `client_id`, user id, outcome; counters for token
issuance and failures by error code via the existing metrics plumbing.

**Out of scope (v1)**: RFC 7592 client management, confidential clients,
scope splitting (`mcp:read`/`mcp:write`), token introspection, Enterprise
Managed Auth, Anthropic connector-directory listing.

## Test suite

New `tests/OAuth.IntegrationTests` (`SharedAppHostTestBase` +
`WebClientTester`, own collection); MCP-side cases extend
`tests/Mcp.IntegrationTests/AuthTest`.

### Layer 1 — protocol tests over raw `HttpClient`

`OAuthTestBase` helpers: `Register()`, `Authorize(session, …)` (follows the
consent redirect by calling `OAuthGrants_Approve` through the tester's
commander), `ExchangeCode()`, `Refresh()`, PKCE pair generator, JWT decoder.

- Metadata: both discovery documents advertise S256, `none`,
  `registration_endpoint`, the CIMD flag, scopes; the protected-resource
  document's `resource` equals `<base>/api/mcp` exactly (both paths).
- DCR: valid → `client_id` + echoed metadata; rejected: non-https non-loopback
  redirect, fragment, `token_endpoint_auth_method=client_secret_post`, unknown
  grant type; rate limit trips.
- Authorize: guest → 302 to `/oauth/consent` with the query intact;
  signed-in without grant → same; after Approve → 302 to `redirect_uri` with
  `code` + `state`; deny → `access_denied` + `state`; unknown client →
  `invalid_client`; bad `redirect_uri` → no redirect; loopback
  `http://localhost/cb` accepts `http://localhost:53211/cb`.
- Token: code + verifier → JWT with `sub`, `sid`, `aud`, `scope`,
  `client_id`, 1 h `exp`; wrong verifier / reused code / wrong `client_id` →
  `invalid_grant`; `resource` reflected as `aud`.
- Refresh: new pair; old refresh token → `invalid_grant`; session
  `LastSeenAt` advanced.
- Revoke: `OAuthGrants_Revoke` → `/api/mcp` rejects the JWT, refresh →
  `invalid_grant`; `/oauth/revoke` with the refresh token → same;
  `Accounts_DeactivateSession` on the backing session → same.
- CIMD: the AppHost serves `/test/cimd/{name}.json`; `client_id=<that URL>` →
  app JIT-created with the document's name/redirects; mismatched inner
  `client_id` → `invalid_client`; unreachable → `invalid_client`; re-fetch
  after `CimdCacheAge`.
- Grants API: `List` shows the grant; invalidation after Approve/Revoke
  (`ComputedTest.When`); Approve as guest → error; Approve idempotent.
- Pruner: orphan DCR app older than `DcrPruneAge` removed, app with a grant
  kept; authorization with an inactive session revoked.
- Sessions: `SessionKind.OAuth` from prefix; `AuthHelper.SignIn` rejects it;
  absent from the device list and the API-keys list; API-key bearer still
  accepted by `/api/mcp`.

### Layer 2 — SDK end-to-end (`McpOAuthClientTest`)

`McpClient` over `HttpClientTransportOptions.OAuth = new ClientOAuthOptions {
DynamicClientRegistration = …, RedirectUri = "http://localhost/cb",
AuthorizationRedirectDelegate = <GET the authorize URL with the tester's
cookie, approve via the commander, return the code from the redirect> }`:
`ListTools` → `post_message` → access token expiry forced via a short
`AccessTokenLifetime` override → the next call refreshes transparently →
`list_messages` sees the post. Second test: two users, two grants, same
client — each sees only their own chats (mirrors `MultiUserAccessTest`).

### Layer 3 — manual acceptance checklist (`docs/oauth.md`)

1. Claude Code vs `https://local.voxt.ai/api/mcp` (`NODE_EXTRA_CA_CERTS`
   pointing at `.config/local.voxt.ai/ssl/local.voxt.ai.crt`): CIMD +
   loopback redirect, consent, tool call.
2. Claude Code vs dev.
3. Cowork / claude.ai custom connector vs dev: "Use Claude's published
   identity" (CIMD), then "Register automatically" (DCR).
4. Cursor vs dev (pure DCR).
5. Revoke from Connected apps → client re-prompts for consent.

Hosted Claude surfaces call the server from Anthropic's infrastructure, so a
local server is reachable only through a public HTTPS tunnel whose URL is
configured as the base URL; otherwise Cowork is tested on dev.
