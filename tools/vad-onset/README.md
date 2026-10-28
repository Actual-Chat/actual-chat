# Voice-onset tuning data

Tools for tuning the voice-start pre-roll trim (`VoiceOnsetFinder` in `Core.Audio`, `voice-onset.ts` on the
web) on real recordings. The recordings come from private chats, so the repo holds only a list of the
messages to use; the audio is downloaded on demand into the git-ignored `tmp/vad-onset/`. Nothing here runs
on CI: `VoicePreRollTrimFittingTest` is a manual test and finds no data there.

## Steps

1. Fetch the audio. You need an admin session with access to the listed chats: copy the value of the
   `FusionAuth.SessionId` cookie from a signed-in browser tab.
   ```bash
   ActualChat_SessionId=<cookie value> python tools/vad-onset/fetch.py
   ```
   `--base-url` (or `ActualChat_BaseUrl`) points it at another environment; the default is `https://voxt.ai`.
   Requires `ffmpeg` on PATH. Rerunning skips what is already there.
2. Remove `Skip = "Manual"` from `VoicePreRollTrimFittingTest` locally and run it. It replays every
   recording through the ONNX VAD, labels the true speech onset before each VAD start, and prints a ranking
   of the trims; it also writes `tmp/vad-onset/cases.tsv`.
3. Optionally, `python tools/vad-onset/labels.py` (needs numpy and matplotlib) builds
   `tmp/vad-onset/labels.html` to check the automatic labels by ear.

## `recordings.tsv`

| Column | Meaning |
|---|---|
| `kind` | `long`: a whole voice message, used for every VAD start after a pause of 0.65 s or more. `soft`: a clip from the end of one word to 1.5 s past the next, which starts with a soft consonant. |
| `chat_id`, `local_id` | The message whose audio to fetch. |
| `start_s`, `end_s` | For `soft` clips, the cut in seconds from the start of the message's audio. |
| `first_letter` | For `soft` clips, the first letter of the word. |

To add recordings, append rows. A `long` message is useful when it has natural pauses of 0.65 s or more
without the recorder stopping (stored pauses show up as runs of exact zeros). A `soft` clip should start in
the real background between two words, not after such a zero run.
