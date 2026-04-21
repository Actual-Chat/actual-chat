# AudioPullTest (TypeScript)

End-to-end smoke test for the TS-pull audio path. Pushes synthetic audio via
`IStreamServer.PushAudio` and subscribes to the same chat via
`ILiveAudioStreams.GetStream`, then prints everything it sees — raw
`LiveStreamItem` wire shape, A_OPUS_S header contents, parsed Opus packets —
so we can diagnose whether the production client's silence is an RPC wiring
bug or a renderer-side issue.

## Run

```bash
# From repo root
npm run test:audio-pull -- -chat:the-actual-one
```

Flags (same style as app.video-load-test):

| Flag | Default | Meaning |
|------|---------|---------|
| `-u:URL` / `-url:URL` | `https://local.voxt.ai` | Server base URL |
| `-chat:ID` / `-chatId:ID` | `the-actual-one` | Chat to push to + pull from |
| `-d:SEC` / `-duration:SEC` | `10` | Run duration, seconds |
| `-email:ADDR` | `test-audiopull@actual.chat` | Dev login (dev OTP 111111 is always accepted) |

## What it validates (in order)

1. Sign-in works and Session is valid.
2. `IStreamServer.PushAudio` accepts an `RpcStream<AudioFrameDto>` with Opus-shaped payloads.
3. `ILiveAudioStreams.GetStream` returns a live stream populated by our push.
4. Stream items deserialize from `[Union]` wire format as `[tag, payload]`.
5. `LiveAudioFrame.Data` carries A_OPUS_S-framed bytes (header + length-prefixed packets).
6. `ActualOpusStreamParser` + `runLiveStreamDemuxer` reproduce the Opus payloads.
