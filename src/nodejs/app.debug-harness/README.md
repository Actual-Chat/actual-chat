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

### Two speakers - the fake microphone

```
npm run harness -- two-speakers [--rounds 3] [--gap 800] [--overlap 1500] [--clips one,two,ak]
                                [--language ru-RU]
```

Signs in the same two users, opens their peer chat, replaces both microphones and
has them talk in turns: A, B, A, ... `--rounds` turns each. The clips are the
transcription tests' own recordings (`tests/Transcription.IntegrationTests/data`),
so the whole pipeline runs for real - VAD, upload, transcription, entries.
`--overlap` starts each turn that long before the previous one ends, so the two
speak at once; without it they're `--gap` ms apart.

The clips are Russian, so the scenario sets the chat's transcription language for
both users (`--language`): left to auto-detection, short utterances come back in
whatever language the detector guessed. A peer chat between two fresh users has no
recorder at all until each has written once - audio permissions are stripped until
the recipient replies or adds the sender to contacts - so the scenario exchanges a
text message first when it finds no recorder.

Recording is billed per second: the scenario stops both recorders in `finally` and
on Ctrl+C, and waits a few seconds of silence first so the last utterance is closed
into an entry.

### Network - latency on the nginx -> Kestrel hop

```
npm run harness -- net on [--latency 150] [--jitter 0]   # a round trip gets 2 x latency
npm run harness -- net set --latency 300                  # on the fly
npm run harness -- net off
npm run harness -- net status
```

`net on` starts Toxiproxy (the `harness` profile in `docker-compose.yml`), proxies
the slot's port P on P+5 and its HTTP/2 sibling on P+6, points the slot's nginx map
(`artifacts/worktree-ports.d/ws<n>.conf`) at the proxy and reloads nginx. That hop
carries every socket the client opens, workers included, plus uploads and media -
unlike DevTools throttling, which slows HTTP but not an open WebSocket's frames.

- It works for `ws<n>` slots only: the main repo's port is set in `nginx.conf`
  itself. The slot comes from the working folder's `.env`.
- `ws <n> <branch>` rewrites the nginx map, so run `net on` again after it.
- Latency only - no bandwidth cap or packet loss yet, though Toxiproxy has both.
- Run network experiments in WASM mode: a Blazor Server circuit has no reconnect
  attempts, so a stalled one reloads the page instead of degrading.

## The page-side surface

`debugUI.fake` is added by `chat-message-editor-debug.ts` and is callable from the
browser console too:

```js
await debugUI.fake.send('hello')
debugUI.fake.attach([{ name: 'a.png', type: 'image/png', base64: '...' }])
debugUI.fake.mic.enable()                    // getUserMedia({ audio }) now returns the fake stream
await debugUI.fake.mic.load('hi', base64)    // any format decodeAudioData takes; returns seconds
await debugUI.fake.mic.say('hi')             // once recording has asked for the mic
await debugUI.setChatLanguage(chatId, 'ru-RU')   // the chat's transcription language
```

`debugUI.fake.mic` comes from `src/nodejs/src/debug-media/fake-microphone.ts`, shared
rather than kept next to the recorder: the video call and the QR scanner also take
the microphone. Nothing is replaced until `enable()` - a developer's own microphone
keeps working on the same build.

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
- No fake camera yet, no scene generator, no chat replay - see
  `tmp/s/2026-09-09-ui-debug-harness-design.md` for the roadmap.
