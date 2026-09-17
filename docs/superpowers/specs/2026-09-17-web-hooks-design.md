# Web hooks — design

**Decided with Alexey on 2026-09-17.** Branch `feat/web-hooks`. Living doc to write on delivery:
`docs/integrations/web-hooks.md` (server side) plus one public docs page (*Integrations &
webhooks*) reachable from Settings → Documents.

## Problem

Voxt has an external write path (MCP + API keys + OAuth) but no way for an external system to be
told when something happens, and no way for a tool that only knows how to `POST` a URL (GitHub,
Grafana, Alertmanager, Power Automate, cron + `curl`) to post into a chat. Enterprise feedback
(Benjamin B, 2026-09) names "easy-to-use webhooks" — Teams-style, paste-a-URL, payload carries
the message — as a precondition for adoption, together with AD/SSO (separate work).
`docs/plans/index.md` lists "Web hook for posts" under *Extensibility / API*.

A concrete internal use case: a personal AI agent that watches a few of my chats and my mentions
and acts via MCP.

## Decisions

1. **Both directions, outgoing first.** Outgoing hooks (Voxt → URL) are the missing capability and
   carry the delivery/security machinery. Incoming hooks (URL → chat) ride on the same
   registration entity and UI and ship second.
2. **Incoming hooks accept the Slack legacy payload** (`text` + `attachments` + `blocks` subset,
   JSON or `payload=` form). It is the de-facto standard: Mattermost and Rocket.Chat accept it
   natively, Discord via `/slack`, and every tool with a "Slack webhook URL" field emits it.
3. **Three scopes, one entity.** Chat hooks (chat settings, `Moderate`+), place hooks (place
   settings, place owner), personal hooks (Settings → API & Apps). Personal hooks are outgoing
   only and subscribe to *my notifications* and/or *selected chats*; MCP + API key already covers
   "post as me".
4. **Full payloads.** Message text travels inline (per-hook opt-out). A thin ids-only envelope
   would force every receiver to hold an API key too, which kills the paste-a-URL simplicity.
5. **No user ids or `/u/…` links in payloads.** Authors are chat-scoped by design; payloads use
   `AuthorId` + name + avatar, exactly like MCP. Directory identity is a v2 field for
   directory-managed places.
6. **No new projects.** Contracts in `Api` / `Api.Contracts` / `Chat.Contracts`, implementation
   in `Chat.Service`, UI in `UI.Blazor.App`. `EgressGuard` and friends move from `Media.Service`
   to `Core.Server`.
7. **Incoming posts come from a bot author**, using the existing negative-local-id convention
   (`Bots.IsBot`), not from the moderator with a name override.
8. **Standard Webhooks** signing and envelope for outgoing deliveries; durable outbox + per-hook
   `Flow` for ordered delivery with multi-hour retries.

## Terminology (user-facing)

- Section: **Integrations** (Mattermost / Rocket.Chat / Discord wording).
- Kinds: **Incoming webhook**, **Outgoing webhook** (Slack / Mattermost wording).
- The incoming create page states: *"Slack-compatible — paste this URL wherever a tool asks for a
  Slack webhook URL."*

## Architecture

```
ChatEntryChanged / ReactionChanged / AuthorUpserted / AuthorsRemoved /
ChatChanged / PlaceChanged / PlaceMembershipChanged   (existing EventCommands)
UserNotifiedEvent                                     (new, from NotificationsBackend.OnNotify)
                     │
                     ▼
        WebHooksBackend.On<Event>            ListActiveByChat(chatId) ∪ ListActiveByPlace(placeId)
                                             ListActiveByUser(userId)  — cached compute methods
        · event-mask + scope filter          · personal "selected chats": Read check per event
        · loop guard (entry.WebHookId != null → no message.* events for any hook)
                     │
                     ▼
        DbWebHookDelivery rows  (outbox, per hook, Seq-ordered, 30-day log)
                     │  resume
                     ▼
        WebHookDeliveryFlow(webHookId) ── EgressGuard ── HttpClient ──► receiver
        · sign, POST, record status/latency · StageResumeIn(backoff) · auto-disable at 72 h

Incoming:  POST /hooks/in/{id}/{token} ──► WebHooksInController
           · SHA-256 token compare · RateLimitIdentity · 64 KB cap
           · SlackPayload → mrkdwn converter → Chats_UpsertEntry (WebHookId set, bot author)

UI:        IWebHooks (List / Get / OnChange / OnRotateSecret / OnTest / ListDeliveries / OnRedeliver)
           WebHookList ← ChatSettings row · PlaceSettings tab · Settings → API & Apps section
```

### Placement

| Piece | Project |
|---|---|
| `WebHook`, `WebHookId`, `WebHookScope`, `WebHookKind`, `WebHookEvents`, `WebHookDisabledReason`, `WebHookDelivery`, `WebHookDeliveryStatus`; `ExternalMessage` (the shared external message model, today `McpChatMessage`) | `Api` |
| `IWebHooks`, `WebHooks_*` commands | `Api.Contracts` |
| `IWebHooksBackend`, `WebHooksBackend_*` commands | `Chat.Contracts` |
| `UserNotifiedEvent` | `Backend/Events` (published by `Notifications.Service`, handled by `Chat.Service`) |
| `WebHooks`, `WebHooksBackend`, `WebHookDeliveryFlow`, `WebHookDeliveryPruner`, `Controllers/WebHooksInController`, `WebHooks/SlackPayload`, `WebHooks/SlackPayloadFlattener` | `Chat.Service` |
| `DbWebHook`, `DbWebHookDelivery` + migration | `Chat.Service/Db`, `Chat.Service.Migration` |
| `EgressGuard`, `EgressHttpHandler`, `SpecialAddresses`, `HostWildcard`, and an `EgressSettings` type holding the allow/deny lists both `Media` and `Chat` read | move to `Core.Server` |
| `StandardWebhookSigner` | `Core.Server` |
| `MrkdwnConverter` (Slack mrkdwn → Voxt markup) | `Core` (MCP `post_message` gains an optional `format: "mrkdwn"`) |
| `WebHookList`, `WebHookKindPage`, `WebHookFormPage`, `WebHookRevealPage`, `WebHookDetailPage`, `WebHookDeliveryList`, BOT chip in the entry header | `UI.Blazor.App` |
| `ChatEntry.WebHookId` (nullable, appended MessagePack key) | `Api` |

Sharding: `ScopeId` is the shard key for hook rows, backend commands and the delivery flow —
chat/place hooks live with the chat, personal hooks with the user.

## Data model

### `WebHook` (client-visible; never carries secrets)

```
WebHookId    Id                      "wh-<16 alnum>"
long         Version
WebHookScope Scope                   Chat | Place | User
string       ScopeId                 ChatId | PlaceId | UserId
WebHookKind  Kind                    Incoming | Outgoing
string       Name                    ≤ 64 chars
UserId       CreatedBy               audit only — ownership is the scope's
Moment       CreatedAt, ModifiedAt
bool         IsEnabled
WebHookDisabledReason DisabledReason None | Manual | DeliveryFailures | UnsafeUrl
Moment?      LastActivityAt          last delivery attempt (out) / last accepted post (in)

-- Outgoing
string       Url                     https:// only (http://localhost on dev instances)
WebHookEvents Events                 flags, see catalog; ≥ 1 required
bool         IncludeText             default true
ApiArray<ChatId> ChatIds             Place: allow-list, empty = all; User: selected chats
bool         SubscribeNotifications  User scope: "My notifications"
string?      CustomHeaderName        value stored only in Db
int          ConsecutiveFailures
int?         LastStatusCode;  string? LastError

-- Incoming
long         BotLocalId              negative, unique per scope; AuthorId = (chatId, BotLocalId)
string       DisplayName             defaults to Name
MediaId?     AvatarMediaId
ChatId?      DefaultChatId           Place scope only
```

Permission to list/create/edit/delete: `Rules.CanModerate()` on the chat; place owner; or
`ScopeId == account.Id`. A personal hook needs `SubscribeNotifications` or ≥ 1 chat id.

### `DbWebHook` — table `WebHooks`

All of the above plus:

```
string?   SecretProtected             outgoing; DataProtection("WebHooks")-encrypted "whsec_…"
string?   PrevSecretProtected         outgoing; valid until PrevSecretExpiresAt (rotation + 24 h)
DateTime? PrevSecretExpiresAt
string?   CustomHeaderValueProtected  encrypted
string?   TokenHash                   incoming; SHA-256(token); unique
```

Indexes: `(ScopeId)`, `(TokenHash)` unique, `(Kind, IsEnabled)`.

### `DbWebHookDelivery` — table `WebHookDeliveries` (outbox and log)

```
string    Id             "dlv-<ulid>" — the webhook-id header; stable across retries
string    WebHookId
long      Seq            per-hook monotonic; delivery order
string    EventType      "message.posted" …
string    Payload        full JSON body, kept for the row's lifetime (Redeliver needs it)
WebHookDeliveryStatus Status   Pending | Succeeded | Failed | Abandoned
int       Attempts;  DateTime? NextAttemptAt
int?      LastStatusCode;  string? LastError;  int? LastLatencyMs
DateTime  CreatedAt;  DateTime? CompletedAt
```

Indexes: `(WebHookId, Seq)`, `(Status, NextAttemptAt)`. `Id` derives from `(WebHookId, eventKey)`
where eventKey = entry id + version / reaction id / author id + version / notification id, so a
replayed NATS event is a no-op insert. `WebHookDeliveryPruner` (shape of `OAuthPruner`) removes
rows older than 30 days. Per-hook cap 10 000 pending rows; beyond it the oldest become
`Abandoned` with error `"queue overflow"`.

### `ChatEntry.WebHookId`

Set by the incoming controller. Used for the `origin` field and the loop guard. The bot badge
needs nothing extra: `Bots.IsBot(entry.AuthorId)`.

### Bot author for incoming hooks

`AuthorsBackend.Get(chatId, authorId)` resolves a negative local id that is not Walle's to the
hook whose `(ScopeId, BotLocalId)` matches (place hooks resolve in every chat of the place) and
returns an `AuthorFull` built from `DisplayName` + `AvatarMediaId`. `AuthorsBackend_Upsert`
already rejects bot ids. Bot authors never appear in member lists or counts (existing
negative-id behaviour).

## Event catalog — `WebHookEvents`

| Flag | Source | Fires when | Scopes |
|---|---|---|---|
| `MessagePosted` | `ChatEntryChangedEvent` Create, or the Update that ends streaming | a text entry exists in final form; voice messages fire once transcription completes | chat, place, user (selected) |
| `MessageEdited` | Update with text/attachments changed, non-streaming | | same |
| `MessageRemoved` | Remove | | same |
| `ReactionAdded` / `ReactionRemoved` | `ReactionChangedEvent` | | same |
| `MemberJoined` / `MemberLeft` | `AuthorUpsertedEvent` (HasLeft flips), `AuthorsRemovedEvent` | | same |
| `ChatUpdated` | `ChatChangedEvent` Update | title / description / picture / kind | same |
| `ChatCreated` / `ChatArchived` | `ChatChangedEvent` Create / Remove for chats of the place | | place |
| `PlaceUpdated` | `PlaceChangedEvent` | | place |
| `PlaceMemberJoined` / `PlaceMemberLeft` | `PlaceMembershipChangedEvent` | | place |
| `Notification` | `UserNotifiedEvent` | anything that would have pushed to my devices | user |
| `Ping` | *Send test event* | | all |

UI groups: Messages · Reactions · Members · Chat · Place · My notifications.

Excluded from v1: read positions, typing, `SpeechStartedEvent` / calls, translations, system
entries (`IsSystemEntry` entries are skipped — member events already cover them).

`UserNotifiedEvent(Notification)` is published by `NotificationsBackend.OnNotify` before the
dormant / soft-update / active-reader logic: a hook wants the event even when the user is
looking at the chat.

## Outgoing delivery

### Request

```
POST <Url>
content-type:      application/json
user-agent:        Voxt-Hooks/1
webhook-id:        dlv-01JAB…
webhook-timestamp: 1758104122
webhook-signature: v1,<base64 HMAC-SHA256(secret, "{id}.{timestamp}.{body}")> [ v1,<prev secret> ]
<CustomHeaderName>: <value>         when set
```

Receivers verify with any Standard Webhooks library, reject timestamps older than 5 min, dedupe
on `webhook-id`.

### Envelope

```json
{
  "id": "dlv-01JAB…",
  "type": "message.posted",
  "timestamp": "2026-09-17T10:15:22.123Z",
  "hook": { "id": "wh-x7k…", "scope": "chat", "scopeId": "s-pmMsV1UVKG-gz3ymbh6n3" },
  "chat": { "id": "s-pmMsV1UVKG-gz3ymbh6n3", "title": "Review Requests", "kind": "place",
            "placeId": "pmMsV1UVKG", "url": "https://voxt.ai/chat/s-pmMsV1UVKG-gz3ymbh6n3" },
  "data": { }
}
```

`type` = flag name in dotted lower case (`message.posted`, `reaction.added`, `member.left`,
`chat.updated`, `place.member.joined`, `notification`, `ping`). `chat` is omitted for
`place.*` and `ping`.

### `data` by family

```jsonc
// message.posted / message.edited
"data": {
  "message": {                                   // ExternalMessage — same shape MCP returns
    "id": 4213, "version": 3, "createdAt": 1758104122123,
    "author": { "id": "s-pmMsV1UVKG-gz3ymbh6n3:12", "name": "Alexey", "avatarUrl": "https://…" },
    "isSystem": false, "isStreaming": false, "isTranscribed": true, "isRemoved": false,
    "text": "PR #4514 is up, please review",     // omitted when IncludeText = false
    "textTruncated": false,                      // present only when truncated at the 256 KB cap
    "attachments": [ { "mediaId": "…", "kind": "image", "fileName": "…", "contentType": "image/png",
                       "length": 1234, "width": 800, "height": 600,
                       "url": "…", "previewUrl": "…", "thumbnailUrl": "…" } ],
    "repliedToId": 4201,
    "mentions": [ "s-pmMsV1UVKG-gz3ymbh6n3:7" ],
    "url": "https://voxt.ai/chat/s-pmMsV1UVKG-gz3ymbh6n3#4213",
    "origin": { "kind": "user" }                 // "user" | "webhook" (+ "webHookId") | "bot"
  },
  "previous": { "version": 2, "text": "PR #4514 is up" }   // message.edited only; text obeys IncludeText
}

// message.removed
"data": { "message": { "id": 4213, "author": { … }, "isRemoved": true } }

// reaction.added / reaction.removed
"data": { "emoji": "👍", "messageId": 4213, "author": { "id": "…:7", "name": "Dima", "avatarUrl": "…" } }

// member.joined / member.left / place.member.joined / place.member.left
"data": { "author": { "id": "…:31", "name": "Benjamin B", "avatarUrl": "…" } }
// place.member.* use the author id in the place's root chat

// chat.updated / place.updated
"data": { "changed": ["title"], "title": "…", "description": "…", "pictureUrl": "…" }

// chat.created / chat.archived
"data": { "chat": { …chat block… } }

// notification
"data": { "kind": "mention",                     // Notification.Kind, lower-cased
          "title": "Dima mentioned you in Review Requests",
          "text": "…", "message": { …ExternalMessage… } }

// ping
"data": { "sentBy": "Alexey" }
```

Attachment URLs are the same `UrlMapper.ContentUrl` values MCP exposes. Body cap 256 KB: text
is truncated and `textTruncated: true` set. No `userId`, no `/u/…` links anywhere.

`McpChatMessage` is replaced by `ExternalMessage` in `Api`; MCP's `authorId`/`authorName` fields
become the `author` object (MCP is pre-release, no compatibility shim).

### Delivery flow

`WebHookDeliveryFlow(webHookId)` (a `Flow`, `[Flow(DelayQuanta = 1)]`):

1. Load the next `Pending` row by `Seq`. None → finish.
2. `EgressGuard.IsAllowed(host)` → resolve, pin the IP, POST through `EgressHttpHandler` with a
   10 s timeout, no redirects, read ≤ 4 KB of the response.
3. 2xx → `Succeeded`, `ConsecutiveFailures = 0`, loop to 1.
4. Network error / 5xx / 429 / timeout → `Failed` attempt, `NextAttemptAt = now + backoff`,
   `StageResumeIn(backoff)`; backoff = 1 m, 5 m, 30 m, 2 h, then 12 h.
5. 3xx, or 4xx other than 429 → `Failed`, no retry, `ConsecutiveFailures++`, loop to 1.
   `410 Gone` → disable the hook immediately.
6. `EgressGuard` rejection at delivery time → `Failed`, disable with `UnsafeUrl`.
7. Head-of-line delivery failing for 72 h → hook disabled (`DeliveryFailures`), every remaining
   `Pending` row `Abandoned`, owner notified, system entry posted in the chat/place.

Re-enabling resets the counter; `Abandoned` rows stay listed and can be redelivered. *Send test
event* bypasses the outbox: in-process POST with the same signer, result shown inline.

### Ordering and duplicates

One flow per hook drains in `Seq` order and stops at the first retrying failure, so a receiver
sees `message.posted` before `message.edited`. Delivery ids derive from the event key, so NATS
redelivery is idempotent on our side and retries reuse the id on the receiver's side.

## Incoming posts

`POST /hooks/in/{id}/{token}`; token also accepted as `Authorization: Bearer <token>`.
`application/json`, or `application/x-www-form-urlencoded` with `payload=<json>`.

| Slack field | Handling |
|---|---|
| `text` | mrkdwn → Voxt markup; required unless `attachments` / `blocks` yield text |
| `attachments[]` | flattened in order: `pretext` · `**title**` + `title_link` as a bare URL · `text` · one `**field.title:** value` line per field · `footer`; `fallback` when the rest is empty; `image_url` → bare URL (never fetched by us); `color`, `author_*`, `thumb_url`, `ts` ignored |
| `blocks[]` | only when `text` is empty: `section.text.text` and `header.text.text` concatenated; other block types ignored |
| `channel` | Place hooks only; must be a chat of the place, else 403; ignored for chat hooks |
| `username`, `icon_url`, `icon_emoji` | ignored (identity is the hook's); documented |
| `thread_ts`, `mrkdwn`, `unfurl_*`, `link_names` | ignored |
| `reply_to` (Voxt extension) | entry local id to reply to |

mrkdwn → Voxt: `*x*`→`**x**`, `_x_`→`*x*`, `~x~`→`x`, inline code and code blocks kept,
`<url|label>`→`label (url)`, `<url>`→`url`, `<mailto:a|b>`→`b (a)`, `<@U…>` / `<#C…>` → literal
text, `<!channel>` / `<!here>` / `<!everyone>` → literal `@channel` (Voxt has no everyone-mention),
`&amp; &lt; &gt;` decoded, `>` line prefix → block quote, list bullets kept. The converter is a
tokenizer, never throws; unrecognised input renders literally.

Effects of an accepted post: `Chats_UpsertEntry` with the bot author `(chatId, BotLocalId)`,
`WebHookId` set on the entry, `LastActivityAt` bumped.

Responses: `200 ok` (text/plain, what Slack and Mattermost return), or
`200 {"ok":true,"chatId":"…","entryId":4213}` when `Accept: application/json`. Errors: `404`
unknown id, bad token or disabled hook (same body); `400` unparseable / no text /
`Chats_UpsertEntry` error text; `403` `channel` outside the place or chat read-only/archived;
`413` > 64 KB; `429` + `Retry-After` (burst 30, sustained 1/s per hook, `RateLimitIdentity`).

## Security

Credentials:

| Credential | Storage | Shown |
|---|---|---|
| Outgoing signing secret `whsec_` + 32 random bytes | DataProtection-encrypted (must be read back to sign) | once, at creation and rotation |
| Incoming token, 32 random bytes base64url | SHA-256 hash, constant-time compare | once, as part of the URL |
| Custom header value | DataProtection-encrypted | once |

Rotation: outgoing — old secret valid 24 h, both signatures sent meanwhile; incoming —
immediate, the URL changes.

Outgoing: `https://` only (loopback `http://` on dev instances); TLS validation always on;
`EgressGuard` on every delivery (public addresses only, `.local` denied, IP pinned, no
redirects); 10 s timeout; response body discarded after 4 KB. Egress IPs documented for
receiver-side allow-listing.

Incoming: 404 for anything credential-related; rate limit; 64 KB cap; converter cannot produce
markup a user couldn't type; no URL in the payload is ever fetched by the controller.

Authorization: hooks belong to the scope — any moderator sees and can delete a colleague's hook;
personal hooks re-check `Read` per event. Per-hook `IncludeText` opt-out for compliance-minded
place admins. E2EE chats (`docs/plans/e2ee.md`) refuse hook registration.

Provenance and loop prevention: `ChatEntry.WebHookId`; outgoing `origin.kind = "webhook"`;
hook-originated entries never produce `message.*` events.

Audit: system entries for created / rotated / disabled / deleted (with actor) in the chat or
place; delivery log per hook, 30 days; secrets never logged.

Deliberately not in v1: payload encryption (JWE), mTLS, per-hook incoming IP allow-lists,
per-post identity overrides.

## Configuration UX

Entry points:

```
Chat settings (dive-in start page, CanModerate)          Place settings (hero + TabPanel)
┌────────────────────────────────────────┐              ┌──────────────────────────────┐
│ 🔑 Chat type                  Private >│              │ [ Members ] [ Integrations ] │
│ 👥 12 members                        > │              └──────────────────────────────┘
│ 🔗 Integrations              2 hooks > │  ← new TileItem, icon-link-2
│ 🕒 Archive chat                        │
└────────────────────────────────────────┘

Settings → API & Apps (existing tab)
  ▸ API keys        (existing)
  ▸ Webhooks        ← new TileTopic; personal hooks
  ▸ Connected apps  (existing)
```

`WebHookList` (one component, scope parameter) follows `ApiKeySettings` / `ConnectedAppsSettings`:
`TileTopic` + `ButtonTile` "Add integration" + a `Tile` of `TileItem`s (icon
`icon-call-arrow-in` / `icon-call-arrow-out`; Content = name; Caption = kind · identity or host ·
last activity or a red failing line; `(Disabled)` suffix). Empty state explains both kinds with a
*Learn more* link to the docs page.

Create flow — dive-in steps like `ApiKeyCreateModal`:

1. **Kind** — two `TileItem`s (skipped for personal hooks).
2. **Form** (`Form` / `FormBlock` / `FormSection` / `TextBox`):
   incoming — Name, Posts as, avatar (`ImageCropPicker`), Default chat (place);
   outgoing — Name, URL, Events (grouped checklist), Include message text, Chats (place: all /
   selected; personal: selected) and My notifications (personal). Validation: URL passes
   `EgressGuard.IsAllowedUri`, name ≤ 64, ≥ 1 event, personal needs notifications or ≥ 1 chat.
3. **Reveal** (`ApiKeyRevealPage` layout + `CopyToClipboard`): incoming — URL, the
   Slack-compatible line, a collapsible `curl`; outgoing — signing secret, *Send test event*.

Detail page: editable fields; Status tile (enabled toggle, last delivery, failure count); Recent
deliveries tile (last 20: time · event · status · latency · *Redeliver*); advanced disclosure
(custom header); danger tiles *Rotate secret* / *Regenerate URL* (`ConfirmModal`, explains the
24 h overlap) and *Delete*.

In-chat: incoming posts show the hook's name + avatar with a **BOT** chip (`Bots.IsBot`);
tapping the name shows "Integration *CI Bot* · added by Alexey · Manage →" (Manage for
moderators). System entries for lifecycle events. Auto-disable also sends the creator a
notification.

Gated behind `EnableIncompleteUI` until complete. All strings via the `L.*` catalog.

## Error handling summary

- Outgoing failures, retries, disable rules, unsafe-URL handling: see *Delivery flow*.
- Hook / chat / place deleted → pending rows dropped; place deletion cascades to the place's
  hooks and the hooks of its chats. Chat archived → outgoing keeps working (archive is not
  deletion); incoming returns 403 without disabling the hook.
- Personal hook loses `Read` on a chat → events skipped silently, no failure counted.
- Burst → outbox grows to the 10 000 cap, then oldest `Abandoned` with `"queue overflow"`.
- Incoming: every failure is a plain HTTP status with a one-line body; nothing is retried on our
  side.

## Reuse

Existing abstractions:

| Need | Reuse |
|---|---|
| SSRF guard, pinned-IP HTTP handler | `EgressGuard`, `EgressHttpHandler`, `SpecialAddresses`, `HostWildcard` (moved to `Core.Server`) |
| Secret encryption | `IDataProtectionProvider` as in `SecureTokensBackend` |
| Random ids / tokens | `RandomStringGenerator`, `Alphabet` |
| Rate limiting | `RateLimitIdentity` |
| Durable retries, ordering | `Flow`, `FlowRuntime.StageResumeIn`, `[Flow(DelayQuanta)]` |
| Posting | `Chats_UpsertEntry` |
| Bot identity | `Bots.IsBot`, negative local ids, `AuthorsBackend.Get` |
| Change commands | `Change<T>` / diff pattern, `ApiCommand<T>` |
| Pruning | `OAuthPruner` shape |
| Reveal-once UI | `ApiKeyRevealPage`, `CopyToClipboard` |
| Settings tiles, dive-in pages | `TileTopic`, `Tile`, `TileItem`, `ButtonTile`, `DiveInDialogPage`, `Form*` |
| External message model | `McpChatMessage` → `ExternalMessage` in `Api` |

New shared components (placed shared, not feature-local): `StandardWebhookSigner`
(`Core.Server`), `MrkdwnConverter` (`Core`), `EgressSettings` (`Core.Server`).

## Testing

| Layer | What |
|---|---|
| Unit | `MrkdwnConverter` table-driven (every mapping, entities, nesting, malformed input); `SlackPayloadFlattener` (attachments, blocks, `payload=` form); `StandardWebhookSigner` against the reference vectors; backoff schedule; delivery-id derivation |
| Unit | `EgressGuard` after the move (existing tests follow it) + DNS-rebinding case |
| Integration | create hook → post → outbox row → flow delivers to an in-test HTTP receiver: signature verifies, three messages arrive in order; 500 → retry row with `NextAttemptAt`; 410 → disabled; 72 h simulated → disabled + abandoned; loop guard; personal hook: mention → `notification`, muted chat → nothing, lost `Read` → skipped; place allow-list; `IncludeText = false` strips text and `previous.text`; rotation sends two signatures |
| Integration | incoming: JSON and form; bad token → 404; rate limit → 429; `channel` inside / outside place; entry has `WebHookId` and a negative-lid author; `AuthorsBackend.Get` resolves the bot author |
| Permissions | non-moderator cannot list/create chat hooks; place hooks need owner; personal hooks invisible to others |
| E2E (Playwright) | Chat settings → Integrations → create incoming → URL revealed once → `curl` → message with BOT chip; create outgoing → *Send test event* shows 200 |
| Manual | one real Slack-format sender (GitHub Actions `slack-send` or Grafana) and one Power Automate "When an HTTP request is received" receiver |

## Delivery plan

1. **Outgoing** — entity + Db + backend + flow + signer + `EgressGuard` move + `UserNotifiedEvent`
   + UI (list, create, reveal, detail, deliveries) for all three scopes + docs page.
2. **Incoming** — token + controller + `SlackPayload` + `MrkdwnConverter` + bot author resolution
   + `ChatEntry.WebHookId` + BOT chip + create/reveal pages for the incoming kind + MCP `format`.

Rough effort: outgoing 7–9 days, incoming 4–5 days.

## Out of scope / later

- Directory identity in payloads (with the AD/SSO work).
- Per-post `username` / `icon_url` overrides; Slack Block Kit beyond section/header text.
- Long-poll / SSE subscription for agents that cannot expose a URL.
- Hook + scoped API key created together for agent onboarding.
- Bot identity for MCP agents (they post as the key's user today).
