# Debug harness

Drives the running app from Node so an investigation doesn't start by rewriting
"connect over CDP, sign in, open a chat, type, send" for the hundredth time.

Attaches to a Chrome you already started; it never launches one. Two-user
scenarios need two browsers with separate profiles:

```
ai chrome*2          # ports 9222 and 9223, anonymous profiles
```

## Scenarios

```
npm run harness -- send-message --chat <chatId> --text "hello"
npm run harness -- attach-image --chat <chatId> --file tmp/photo.jpg
npm run harness -- two-users [--text "hello"] [--file tmp/photo.jpg]
```

`two-users` signs in `+1 555 555 5550` and `+1 555 555 5551` on the two browsers,
derives their peer chat id, and has each send a message. Pass `--chat` to use an
existing chat instead.

Options: `--port` / `--port2` (CDP ports, default 9222 / 9223), `--user` /
`--user2` (phone or email), `--chat`, `--fresh` (sign out first rather than
reusing whoever is signed in).

## The page-side surface

`debugUI.fake` is added by `chat-message-editor-debug.ts` and is callable from the
browser console too:

```js
await debugUI.fake.send('hello')
debugUI.fake.attach([{ name: 'a.png', type: 'image/png', base64: '...' }])
```

It exists only where `window.debugUI` does — `DebugUI` is resolved only when the
host is not a production instance (`AppScopedServiceStarter.cs`), so nothing here
is reachable in production. It adds no code to the chat editor itself.

**It drives the DOM, not the `ChatMessageEditor` JS object.** `send` writes a text
node and dispatches `Enter`, `attach` sets `files` on the real `<input type=file>`
and dispatches `change` — the same entry points a keystroke and a file pick use, so
a scenario fails wherever a human would. Calling the instance's own methods would
skip exactly the wiring most bugs live in.

## What it does not do

- **It is not a full keystroke simulator.** `send` replaces the editor content
  with one text node, so `@alice` stays literal text rather than becoming a
  mention — that needs a real pass through the mention list.
- **Plain `Enter` posts on desktop only.** On a mobile layout `MarkupEditor`
  treats bare `Enter` as a line break, so `send` will not post there.
- **The picker dialog is bypassed, not the upload.** Everything downstream of the
  picker — registry, Blazor callback, upload session — runs for real.
- No fake microphone or camera yet, no chat replay, no network shaping — see
  `tmp/s/2026-09-09-ui-debug-harness-design.md` for the roadmap.
