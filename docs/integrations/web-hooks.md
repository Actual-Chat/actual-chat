# Integrations: web hooks

**Status: phase 1 (outgoing) and phase 2 (incoming, chat scope).**

Voxt can call a URL you choose whenever something happens in a chat, a place, or
your own notifications — no API key required on the receiving end, just a plain
`POST` with a JSON body and a [Standard Webhooks](https://www.standardwebhooks.com/)
signature. This page is the receiver-facing reference: what a delivery looks
like, how to verify it, and the retry/disable rules. It ends with a short
section for people managing hooks in the Voxt UI.

[[toc]]

## Scopes

A web hook belongs to one of three scopes, chosen at creation:

| Scope | Created from | Subscribes to |
|---|---|---|
| **Chat** | Chat settings → Integrations (needs `Moderate`) | that chat's message/reaction/member/chat-updated events |
| **Place** | Place settings → Integrations (place owner) | the place's own events, plus its chats' events — either all chats or an allow-list (`ChatIds`; empty means all) |
| **Personal** | Settings → API & Apps → Webhooks | your notifications (`Notification` event, if subscribed) and/or a hand-picked set of chats you can read |

A personal hook is re-checked against your `Read` permission on each source
chat when an event is enqueued for it (fan-in time, not delivery time); if
you've lost access, that event is skipped silently — it never counts as a
delivery failure. A chat you no longer read produces no events for a personal
hook, and archiving a chat doesn't stop its outgoing hooks (archiving isn't
deletion).

Source:
[WebHook.cs](https://github.com/Actual-Chat/actual-chat/blob/main/src/dotnet/Api/WebHooks/WebHook.cs),
[WebHooksBackend.Events.cs](https://github.com/Actual-Chat/actual-chat/blob/main/src/dotnet/Chat.Service/WebHooks/WebHooksBackend.Events.cs).

## Event catalog

A hook fires only for the event types selected in its `Events` flags. `type`
is the flag name in dotted lower case, exactly as sent on the wire:

| `type` | Fires when | Scopes |
|---|---|---|
| `message.posted` | A text entry exists in final form — typed messages post immediately; a voice message posts once transcription finishes and it stops streaming | chat, place, personal |
| `message.edited` | Non-streaming text or attachments change | chat, place, personal |
| `message.removed` | An entry is soft-removed | chat, place, personal |
| `reaction.added` / `reaction.removed` | A reaction is added or removed | chat, place, personal |
| `member.joined` / `member.left` | An author joins or leaves the chat | chat, place, personal |
| `chat.updated` | Title, description, picture, or public/private visibility changes | chat, place, personal |
| `chat.created` / `chat.archived` | A chat of the place is created or archived | place |
| `place.updated` | The place's title, description, or picture changes | place |
| `place.member.joined` / `place.member.left` | Someone joins or leaves the place | place |
| `notification` | Anything that would otherwise have pushed to your devices (a personal hook needs "My notifications" subscribed) | personal |
| `ping` | *Send test event*, from the hook's detail page | all |

System entries (`IsSystemEntry`) never generate `message.*` events — member
events already cover joins/leaves. Read positions, typing, calls, and
translations don't have hook events in phase 1.

## Request

```
POST <your URL>
content-type:      application/json
user-agent:        Voxt-Hooks/1
webhook-id:        <delivery id>
webhook-timestamp: <unix seconds>
webhook-signature: v1,<base64 HMAC-SHA256> [v1,<base64 HMAC-SHA256 with the previous secret>]
<custom header>:   <value>            — only if you set one
```

`webhook-id` is stable across retries of the same delivery, and its value is
also the envelope's `id` field below — dedupe on it. Its actual shape is
`<hookId>:<type>:<eventKey>` (e.g. `aB3xY9pQmN2kLd7f:message.posted:4213:3`,
where the trailing `4213:3` is the entry's local id and version) — treat it as
an opaque string, not a fixed-format id. A redelivery (see below) reuses the
same id with a `:rN` suffix.

`webhook-signature` carries **two** signatures for 24 hours after a secret
rotation — the new secret and the old one — so verifying against either one
of the space-separated `v1,…` parts is enough to accept the delivery through
the overlap window.

## Verifying the signature

The scheme is exactly [Standard Webhooks](https://www.standardwebhooks.com/):
HMAC-SHA256 over `{id}.{timestamp}.{body}`, using the raw bytes behind your
`whsec_…` secret (base64-decode after the prefix), and comparing in constant
time. Reject a timestamp older than 5 minutes to guard against replay.

::: code-group

```csharp [C#]
using System.Security.Cryptography;
using System.Text;

bool Verify(string secret, string id, string timestamp, string body, string sigHeader) {
    var key = Convert.FromBase64String(secret["whsec_".Length..]);
    var payload = Encoding.UTF8.GetBytes($"{id}.{timestamp}.{body}");
    var expected = "v1," + Convert.ToBase64String(HMACSHA256.HashData(key, payload));
    return sigHeader.Split(' ').Any(p => CryptographicOperations.FixedTimeEquals(
        Encoding.UTF8.GetBytes(p), Encoding.UTF8.GetBytes(expected)));
}
```

```js [Node]
const crypto = require('crypto');

function verify(secret, id, timestamp, body, sigHeader) {
  const key = Buffer.from(secret.slice('whsec_'.length), 'base64');
  const payload = `${id}.${timestamp}.${body}`;
  const expected = 'v1,' + crypto.createHmac('sha256', key).update(payload).digest('base64');
  return sigHeader.split(' ').some(p =>
    p.length === expected.length && crypto.timingSafeEqual(Buffer.from(p), Buffer.from(expected)));
}
```

```python [Python]
import hmac, hashlib, base64

def verify(secret, id, timestamp, body, sig_header):
    key = base64.b64decode(secret.removeprefix("whsec_"))
    payload = f"{id}.{timestamp}.{body}".encode()
    digest = hmac.new(key, payload, hashlib.sha256).digest()
    expected = "v1," + base64.b64encode(digest).decode()
    return any(hmac.compare_digest(p, expected) for p in sig_header.split(" "))
```

:::

Any existing Standard Webhooks verification library works unchanged, since
the scheme is unmodified.

Source:
[StandardWebhookSigner.cs](https://github.com/Actual-Chat/actual-chat/blob/main/src/dotnet/Core.Server/Security/StandardWebhookSigner.cs).

## Envelope

```json
{
  "id": "aB3xY9pQmN2kLd7f:message.posted:4213:3",
  "type": "message.posted",
  "timestamp": "2026-09-17T10:15:22.123Z",
  "hook": { "id": "aB3xY9pQmN2kLd7f", "scope": "chat", "scopeId": "s-pmMsV1UVKG-gz3ymbh6n3" },
  "chat": { "id": "s-pmMsV1UVKG-gz3ymbh6n3", "title": "Review Requests", "kind": "place",
            "placeId": "pmMsV1UVKG", "url": "https://voxt.ai/chat/s-pmMsV1UVKG-gz3ymbh6n3" },
  "data": { }
}
```

- `hook.scope` is `chat`, `place`, or `user` (the personal scope). For a
  personal hook `hook.scopeId` is the owner's own user id — the only user id
  that ever appears in a payload.
- `chat.kind` is `group`, `peer`, `place`, or `thread`; `placeId` is present
  only for a chat that belongs to a place.
- The `chat` field is omitted entirely for `place.*` events and for `ping`
  (there's no single chat to attach).
- Every field that has no value is omitted from the JSON rather than sent as
  `null`.

## `data` by event family

### `message.posted` / `message.edited`

```jsonc
"data": {
  "message": {                                    // ExternalMessage — same shape MCP returns
    "id": 4213, "version": 3, "createdAt": 1758104122123,
    "author": { "id": "s-pmMsV1UVKG-gz3ymbh6n3:12", "name": "Alexey", "avatarUrl": "https://…" },
    "isSystem": false, "isStreaming": false, "isTranscribed": true, "isRemoved": false,
    "text": "PR #4514 is up, please review",       // omitted when the hook's "Include message text" is off
    "textTruncated": true,                         // present, and true, only when the 256 KB cap truncated it
    "attachments": [ { "mediaId": "…", "kind": "image", "fileName": "…", "contentType": "image/png",
                       "length": 1234, "width": 800, "height": 600,
                       "url": "…", "previewUrl": "…", "thumbnailUrl": "…" } ],
    "repliedToId": 4201,
    "mentions": [ "s-pmMsV1UVKG-gz3ymbh6n3:7" ],
    "url": "https://voxt.ai/chat/s-pmMsV1UVKG-gz3ymbh6n3?n=4213",
    "origin": { "kind": "user" }
  },
  "previous": { "version": 2, "text": "PR #4514 is up" }    // message.edited only; text obeys the same opt-out
}
```

`origin.kind` is one of:

| Value | Meaning |
|---|---|
| `user` | Typed by a person in a client |
| `api` | Posted through the API (MCP, an API key, `post_message`, …) |
| `bot` | Posted by a bot author (negative local author id) |
| `webhook` | Posted through an incoming web hook; `origin.webHookId` is that hook's id |

### `message.removed`

```json
"data": { "message": { "id": 4213, "author": { "id": "…", "name": "…" }, "isRemoved": true } }
```

### `reaction.added` / `reaction.removed`

```json
"data": { "emoji": "👍", "messageId": 4213, "author": { "id": "…:7", "name": "Dima", "avatarUrl": "…" } }
```

### `member.joined` / `member.left` / `place.member.joined` / `place.member.left`

```json
"data": { "author": { "id": "…:31", "name": "Benjamin B", "avatarUrl": "…" } }
```

`place.member.*` uses the author id in the place's root chat.

### `chat.updated` / `place.updated`

```json
"data": { "changed": ["title"], "title": "…", "description": "…", "pictureUrl": "…", "isPublic": true }
```

`title`, `description`, `pictureUrl`, and `isPublic` are always the
chat's/place's current values; `changed` lists which of the four differ from
before the update.

### `chat.created` / `chat.archived`

```json
"data": { "chat": { "id": "…", "title": "…", "kind": "group", "placeId": "…", "url": "…" } }
```

### `notification`

```json
"data": { "kind": "mention", "title": "Dima mentioned you in Review Requests",
          "text": "…", "message": { /* ExternalMessage, or absent for a non-message notification */ } }
```

`kind` is `Notification.Kind` lower-cased with no separators (e.g. `message`,
`reply`, `mention`, `reaction`, `attention`, `thread`, `conversation`).

### `ping`

```json
"data": { "sentBy": "Alexey" }
```

## Size cap

The full envelope is capped at **256 KB**. If a message's text pushes the
payload past the cap, the text is progressively shortened and
`message.textTruncated: true` is set; every other field is kept as-is.
`textTruncated` is present in the payload only when this happened — its
absence means the text (if any) is complete.

## Delivery semantics

- **Ordering.** One flow per hook drains its outbox in the order events were
  enqueued (`Seq`), and stops at the first delivery that has to wait for a
  retry — so deliveries arrive in outbox order per hook. Ordering across
  near-simultaneous events is best-effort: the events are fanned in
  concurrently, so two changes made within the same instant can be enqueued
  in either order.
- **Retries.** A network error, a DNS failure, a timeout, a `429`, or a `5xx`
  response is retried with backoff **1 min → 5 min → 30 min → 2 h → 12 h**,
  then repeats at 12 h.
- **Terminal failures.** Any other non-2xx status (a `4xx` other than `429`,
  or a `3xx` — redirects are never followed) fails that one delivery
  permanently and moves on to the next; it does **not** retry and does not by
  itself disable the hook.
- **`410 Gone`** disables the hook immediately (`DeliveryFailures`).
- **Auto-disable.** If the head-of-line delivery keeps failing for **72
  hours** straight (counted from the later of the delivery's creation and the
  hook's last edit or re-enable), the hook is disabled and every remaining
  pending delivery is marked `Abandoned`. Re-enabling the hook does **not**
  resume those abandoned rows — use *Redeliver* on the ones you still want.
  A manual disable leaves pending rows pending, so re-enabling resumes them.
- **Unsafe URL.** If the URL fails the egress check at delivery time (it now
  resolves to a private/loopback/link-local address, for example), that
  delivery fails and the hook is disabled with reason `UnsafeUrl`. A host
  that merely doesn't resolve is a network failure and is retried like one.
- **Redelivery.** A completed (non-pending) delivery can be redelivered from
  its detail page; the clone reuses `<originalId>:r<attempts>` as its id, so
  redelivering the same completed delivery twice is a no-op.
- **Idempotency.** The delivery id is derived from the event itself (entry id
  + version, reaction id + version, author id + version, or notification
  id), so a
  redelivered NATS event on our side is a harmless no-op insert — and the
  same id on retries lets you dedupe on `webhook-id`.
- **Queue overflow.** A hook's outbox caps at **10,000** pending rows; past
  that, the oldest pending row is marked `Abandoned` with error
  `"queue overflow"` to make room.
- **Timeout.** Each attempt gets **10 seconds**; the response body is read up
  to **4 KB** for error logging and then discarded.
- **Test event.** *Send test event* bypasses the outbox entirely — an
  in-process signed POST, with the result (status/latency/error) shown
  inline, not logged as a delivery.

Source:
[WebHookDeliverer.cs](https://github.com/Actual-Chat/actual-chat/blob/main/src/dotnet/Chat.Service/WebHooks/WebHookDeliverer.cs),
[WebHookDeliveryFlow.cs](https://github.com/Actual-Chat/actual-chat/blob/main/src/dotnet/Chat.Service/Flows/WebHookDeliveryFlow.cs),
[Constants.cs](https://github.com/Actual-Chat/actual-chat/blob/main/src/dotnet/Api/Constants.cs)
(`WebHooks` nested class).

## Delivery log

The last **20** deliveries are visible on a hook's detail page (time, event,
status, latency, and a *Redeliver* action for a completed one). A run of
consecutive deliveries with the same event, status and error collapses into one
row with a `×N` count, and *Redeliver* on that row redelivers the whole run. The
underlying log is kept for **30 days** before an hourly sweep prunes it — a
still-`Pending` row is never pruned.

## Security

- **Secrets.** The signing secret (`whsec_` + 32 random bytes) is shown once,
  at creation and again on rotation, and stored encrypted; it has to be read
  back to sign each request, so it's never write-only. A custom header value
  is stored encrypted the same way.
- **Rotation overlap.** Rotating the secret keeps the old one valid for
  **24 hours**; during that window every request carries both signatures (see
  the `webhook-signature` header above), so you can roll your verifier over
  without dropping deliveries.
- **URL rules.** Outgoing URLs must be `https://` and use a host name — an
  IP-literal host or an internal domain is rejected at save time. Plain
  `http://` is accepted only for a loopback address, and only on a
  development or test instance of Voxt — never in production. The rules are
  re-checked at delivery time, not just at save time, so a URL that later
  resolves somewhere unsafe gets caught and the hook disabled, not silently
  delivered to.
- **Custom header hygiene.** The header name must be a plain HTTP token
  (letters, digits, and `` !#$%&'*+-.^_`|~ ``), the value can't contain line
  breaks or control characters, and neither can reuse `webhook-id`,
  `webhook-timestamp`, `webhook-signature`, `user-agent`, `content-type`,
  `host`, `content-length`, `transfer-encoding`, or `connection`.
- **No user ids.** Authors are represented exactly like MCP represents them
  — a chat-scoped author id, display name, and avatar URL. Payloads never
  contain a `userId` or an `/u/…` profile link; the one user id on the wire
  is a personal hook's `hook.scopeId`, which is the owner's own.
- **Compliance opt-out.** `IncludeText` can be turned off per hook; when it
  is, `text` and `previous.text` are both omitted, but the rest of the
  envelope (author, attachments, reaction emoji, etc.) still arrives.

### Egress IPs

Voxt does not yet publish a fixed list of source IP addresses for outgoing
deliveries. If your receiver needs an IP allow-list, hold off on locking one
down — the addresses will be published alongside the release that makes this
guarantee.

Source:
[WebHooksBackend.cs](https://github.com/Actual-Chat/actual-chat/blob/main/src/dotnet/Chat.Service/WebHooks/WebHooksBackend.cs),
[IWebHooks.cs](https://github.com/Actual-Chat/actual-chat/blob/main/src/dotnet/Api.Contracts/WebHooks/IWebHooks.cs).

## Incoming hooks

An incoming hook is a secret URL bound to one chat. Any `POST` to it posts a
message into that chat under the hook's own bot, which has the display name
and avatar you set on the hook and appears in the member list.

### URL and auth

`POST https://<host>/hooks/in/<token>`, where `token` is `whin_` followed by 43 characters.
The token is shown once at creation and again on *Rotate URL*; Voxt stores
only its hash. Rotation replaces it immediately — there is no overlap window.

### Body

`Content-Type: application/json`, or `application/x-www-form-urlencoded`
with a `payload` field holding the same JSON (the Slack and Mattermost
legacy form). Body cap 64 KB.

| Field | Meaning |
|---|---|
| `text` | Voxt markup. Optional when a card yields text |
| `replyTo` | Local id of the message to reply to |
| `attachments` | Slack legacy cards: `pretext`, `title`, `title_link`, `text`, `fields[{title,value}]`, `image_url`, `thumb_url`, `footer`. `color`, `ts`, `author_*`, `fallback`, `mrkdwn_in`, `username`, `icon_*` and `channel` are ignored |

Each card folds into the message in the order pretext, **title** with its
link, text, one `Title: value` line per field, footer, separated from `text`
and from each other by a blank line. `image_url` and `thumb_url` are fetched
through the egress guard and attached as images, at most 4 per message; a
failed fetch drops that image and posts the rest.

### Responses

| Status | When |
|---|---|
| `200 {"ok":true,"id":<local id>}` | Posted |
| `400 {"ok":false,"error":"…"}` | Unparseable body, nothing to post, unknown `replyTo`, text over the message cap |
| `404` | Unknown or malformed token |
| `410 {"ok":false,"error":"This web hook is no longer available."}` | Hook disabled, chat archived or deleted, or the bot removed from the chat |
| `413` | Body over 64 KB |
| `429 {"ok":false,"error":"Too many posts. Please retry later."}` + `Retry-After` | More than 60 posts in a minute for this hook, or 600 in an hour |
| `503 {"ok":false,"error":"Temporarily unavailable, retry later."}` | Backend failure; safe to retry |

The bot cannot sign in, read anything or be addressed; the URL is the only
way it acts, and nothing in the payload can change its name or picture.
Messages it posts carry `origin.kind = "webhook"` with the hook's id in
outgoing payloads and MCP.

## Managing hooks

Hooks live under three entry points, depending on scope:

- **Chat settings → Integrations** (visible to moderators) — chat-scoped hooks. *Add integration* offers an outgoing or an incoming webhook; an incoming hook's detail page has *Post test message*, *Rotate URL* and *Delete* (the bot leaves the chat, its messages stay).
- **Place settings → Integrations tab** (place owners) — place-scoped hooks,
  with an optional chat allow-list.
- **Settings → API & Apps → Webhooks** — your personal hooks, next to API
  keys and connected apps. The same list continues with every chat and place
  hook you created, each captioned with its chat or place, so one page shows
  everything that calls out on your behalf. A hook in a chat you no longer
  moderate stays listed, read-only, so you still know your endpoint is being
  called.

Creating an outgoing hook walks through a name, the URL, which events to
subscribe to (grouped as Messages / Reactions / Members / Chat / Place / My
notifications), whether to include message text, and — for place and
personal hooks — which chats it applies to. The signing secret is shown once,
right after creation, together with a *Send test event* action.

A hook's detail page shows its status (enabled/disabled, last delivery,
consecutive failures), its recent delivery log, and — for an outgoing hook —
*Rotate secret* and *Delete*, both behind a confirmation. Disabling a hook
(manually, or automatically after repeated failures) stops new deliveries but
keeps its history and configuration. Re-enabling after a manual disable
resumes the deliveries that were left pending; after an auto-disable the
pending ones were already marked `Abandoned`, so only new events flow and
anything you still need has to be *Redeliver*ed by hand.
