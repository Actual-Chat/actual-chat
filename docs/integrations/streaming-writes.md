# Integrations: streaming writes

**Status: shipped.**

An integration can open a message, add to it over time, and close it. The
message appears in the chat on the first call and fills in live for everyone
watching — the same way a transcript grows word by word while someone is still
speaking, because it is the same machinery underneath.

This is what you want for anything that produces text progressively: an LLM
emitting tokens, a long job reporting as it goes, a translation arriving clause
by clause. The alternative — posting the finished text after a pause, or
editing one message repeatedly — reads as a bot either way.

[[toc]]

## Two ways in

| Your transport | Use | Shape |
|---|---|---|
| Can hold a stream open (a .NET client over the Voxt RPC API) | `IChats.StreamEntry` | hand it an `RpcStream<string>`, await one result |
| Cannot (MCP, plain HTTP) | `start_message_stream` → `append_message_stream` → `finish_message_stream` | one call per piece, each returns where the server is |

Voice mirrors both: `IChats.StreamVoice` for the first, `start_voice_stream` →
`append_voice_stream` → `finish_voice_stream` for the second.

Both end in the same place: one ordinary message, authored by you, with the
full text as its content. Pick the first if you can — it is one call and the
server handles pacing. Pick the second when a request/response protocol is all
you have.

## MCP tools

### `start_message_stream`

| Parameter | Type | Meaning |
|---|---|---|
| `chatId` | `string` | The chat to post in |
| `entryId` | `long?` | Stream into one of *your own* recent messages instead of posting a new one |

Returns `{ streamId, entryId, offset, isFinished }`. The message exists as soon
as this returns — `entryId` is its LID, usable with `list_messages`,
`react`, `pin_message` and the rest right away.

`streamId` is your handle. It is not the id of the underlying content stream
and cannot be used to subscribe to anything.

### `append_message_stream`

| Parameter | Type | Meaning |
|---|---|---|
| `streamId` | `string` | From `start_message_stream` |
| `offset` | `int` | Characters the server already has |
| `text` | `string` | The piece to add |

Returns the same shape, with `offset` advanced.

**The offset is the whole retry story.** If your `offset` doesn't match the
server's, nothing is written and the server's offset comes back — so a call you
retried, or one whose response you lost, is harmless: send it again with the
offset you were last told, and carry on from whatever comes back. This is the
same contract `append_upload` uses for files.

### `finish_message_stream`

Takes `streamId`, settles the message on the text received so far, and returns
the final state with `isFinished: true`. Calling it again within the idle
window returns the same result rather than an error, so a retry is safe.

There is no "cancel". To discard the message, finish the stream and then
`remove_message` — nothing else can be posted into a stream once it is closed.

## Rules

**Ownership.** Only the account that opened a stream can append to it or finish
it. Everyone else gets an error, including other members of the same chat.

**Attribution.** The message is authored by the authenticated caller. There is
no impersonation on this path. A stream opened with an API key or an OAuth
token is marked as posted via the API, exactly as `post_message` would be.

**Abandonment.** A stream with no `append` for **90 seconds** is finished for
you with whatever arrived, and a stream is closed after **30 minutes**
regardless of how busy it is. A producer that crashes mid-sentence therefore
leaves a readable, settled message — never a message that streams forever. The
cost of relying on this is 90 seconds of a message looking unfinished, so call
`finish_message_stream` when you are done.

**Size.** A streamed message obeys the ordinary message cap of 64 KB; an append
that would cross it fails and writes nothing.

**Permissions and maintenance.** `Write` permission in the chat is required, and
it is re-checked as the text flows, not only at `start`: if the chat enters
maintenance mid-stream, the stream stops there and the message settles on what
had already arrived.

**Streaming into an existing message.** Pass `entryId` to rewrite one of your
own messages progressively. It must be yours, not forwarded, not already
streaming, and **less than 15 minutes old** — past that a message is settled
enough that re-animating it reads as a glitch, so use `edit_message` instead.

## What readers see

Text is rendered as it arrives, as markup rather than as raw characters. A
half-arrived `**bold` renders bold immediately instead of showing the asterisks
and snapping when the closing token lands, and — this one matters — a
`||spoiler` is masked from the first character rather than sitting in the clear
until the stream ends.

Markup that spans the whole message still needs the whole message: a list or a
code block only looks right once its lines have arrived. Text is the common
case and it behaves.

## Example: stream an LLM response into a chat

Needs `pip install mcp openai`, an OpenAI key, and a Voxt API key from
**Settings → API & Apps**.

```python
import asyncio, os
from mcp import ClientSession
from mcp.client.streamable_http import streamablehttp_client
from openai import AsyncOpenAI

VOXT_MCP = "https://voxt.ai/mcp"
CHAT_ID = os.environ["VOXT_CHAT_ID"]
HEADERS = {"Authorization": f"Bearer {os.environ['VOXT_API_KEY']}"}

async def call(session, name, **args):
    result = await session.call_tool(name, args)
    if result.isError:
        raise RuntimeError(f"{name} failed: {result.content}")
    return result.structuredContent

async def main():
    openai = AsyncOpenAI()
    async with streamablehttp_client(VOXT_MCP, headers=HEADERS) as (read, write, _):
        async with ClientSession(read, write) as session:
            await session.initialize()

            stream = await call(session, "start_message_stream", chatId=CHAT_ID)
            stream_id, offset = stream["streamId"], stream["offset"]
            try:
                completion = await openai.chat.completions.create(
                    model="gpt-4o-mini",
                    messages=[{"role": "user", "content": "Explain TCP slow start, briefly."}],
                    stream=True,
                )
                async for chunk in completion:
                    piece = chunk.choices[0].delta.content
                    if not piece:
                        continue
                    # The returned offset is the only one to trust: on a retry or a
                    # lost response the server's offset is what comes back.
                    stream = await call(session, "append_message_stream",
                                        streamId=stream_id, offset=offset, text=piece)
                    offset = stream["offset"]
            finally:
                # Always: without it the message sits unfinished for 90 seconds.
                final = await call(session, "finish_message_stream", streamId=stream_id)

            print(f"posted message {final['entryId']}, {final['offset']} chars")

asyncio.run(main())
```

Run it with the chat open in another window: the message appears empty the
moment `start_message_stream` returns and then writes itself out.

**Don't send one append per token.** The server coalesces outbound updates to
five per second no matter how fast you push, so a token-at-a-time loop costs
you round trips and buys nothing. Batch a few tokens, or flush on a short timer
— 50–100 ms is plenty.

**If you want to be heard as well as read, write at speaking pace.** Someone
listening to the chat hears this message in a synthesized voice, and the sound
plays at speaking rate whatever rate you produced it at. Text sent faster than
that is spoken after it appears, and the gap grows for as long as you outrun
the voice — writing English at 19 characters a second puts the voice four
seconds behind over half a minute.

**The server tells you how far behind the voice is.** Every reply carries
`speechBacklog`: the seconds of speech a listener still has to hear before
reaching what you just wrote. Slow down while it grows; it is null when nothing
is speaking this entry, which is also the answer "nobody is listening, write as
fast as you like". Prefer it to any rate you could look up — it is measured
from the voice actually speaking, so it is right for the language, the voice and
the provider, and it stays right when any of them change.

Speech also starts as the text does rather than when the first listener asks,
so long as someone is already in the room — the provider's start-up no longer
lands on the first words.

**If you cannot use the backlog, speaking rate is per language, not universal.** A character carries far more
sound in a character-dense script, so the same number of characters is a very
different amount of speech:

| Language | ~chars/s | vs English |
|---|---|---|
| Russian | 17 | 1.2× |
| English | 15 | 1.0× |
| Japanese | 6 | 0.4× |
| Mandarin | 4 | 0.3× |

Pacing Mandarin at the English number writes about three and a half times
faster than the voice can say it, and the gap never stops growing.

Measure it yourself for a language or voice not listed — the method is one
request: send a passage to the synthesizer, divide its character count by the
duration of the audio that comes back. The numbers above were measured that
way, on two passages per language, and were stable across both.

A producer that wants to be read quickly should ignore all of this and write as
fast as it likes; readers are not waiting on a voice. The choice is yours to
make, but it is a choice.

## Voice: streaming what you say

The same lease, carrying sound. `start_voice_stream` →
`append_voice_stream` → `finish_voice_stream` posts a voice message, and a
listener hears it live rather than after you finish.

An append carries audio, text, or both, and which of the three you send decides
what happens to your message:

| You send | Server runs | Result |
|---|---|---|
| text only (`start_message_stream`) | speech synthesis, for listeners | your words, read aloud in a generated voice |
| audio only | nothing | a playable message with no transcript |
| audio and text | nothing | your voice, and a transcript that is exactly your words |

The third is the one to reach for if you have both. Neither recognition nor
synthesis stands between what you produced and what arrives, so the transcript
is not a guess at your words and the voice is not an approximation of yours.

### The audio contract

Ogg Opus, and **every packet must hold one 20 ms frame**. That is the format a
live listener is played without re-encoding, so a 40 ms or 60 ms packet is
rejected and takes the stream with it. Most encoders already default to 20 ms;
the flags that force it are:

```bash
ffmpeg -i in.wav -c:a libopus -frame_duration 20 out.opus
opusenc --framesize 20 in.wav out.opus
```

Send the bytes in order. A chunk may split an Ogg page anywhere — the server
buffers the remainder until the bytes completing it arrive — so chunk by
whatever size suits your transport, up to 1 MB decoded per call.

### Pacing

**Deliver at least as fast as the audio plays.** Someone is listening while it
arrives, and audio that turns up slower than real time is a gap in the middle
of a sentence. Faster is fine: the server holds what it has. At the 32 kbps a
speech encoder typically produces, one 4 KB chunk per second is real time.

### Pacing the text against the voice

Send a clause when the audio that carries it is going out, not on a fixed cadence.
Live listeners see the text the moment it arrives and hear the audio when playback
reaches it, so a schedule that ignores where the words fall in the sound drifts
against it — clauses are not equal in length, and spacing them evenly puts each one
on screen after it has been spoken.

`audioOffset` does not fix this. It writes the time map, which drives seeking and
highlighting on the *finished* message; live rendering shows whatever text has
arrived. For a listener following along, the send time is the only lever.

If you have alignment, use it. If you don't, character count is a good stand-in —
speech runs at a roughly constant number of characters per second, so a clause
starting `n` characters into a message of `N` belongs at about `n/N` of its audio.

### Reading the reply

Every call returns `{ streamId, entryId, textOffset, audioBytes,
audioDuration, isFinished }`.

`audioDuration` is what the server **decoded**, not what you sent. If it stops
growing while `audioBytes` keeps climbing, your audio is not being read — check
the packet size first. This is the only signal that distinguishes a message
that will play from one that will be silent.

`entryId` is null until `finish_voice_stream` returns it: the entry is created
by the audio pipeline once there is something to post, and a stream that
carried neither audio nor words posts nothing and finishes with `entryId`
still null.

`textOffset` works exactly as `offset` does for a text stream, with the same
retry story. Audio has no equivalent check — it is append-only, so a chunk you
send twice is heard twice.

### Language

`start_voice_stream` takes a `language` (`"en-US"`, `"de-DE"`, …), as
`start_message_stream` does. It is what listeners are offered a translation
*from*; omit it and the server falls back to the chat's language, then to your
own. Nothing reads your words on this path, so an undeclared message is one
whose language the server can only guess at.

### Example

```python
stream = mcp("start_voice_stream", chatId=chat_id, language="en-US")
sid = stream["streamId"]

# 32 kbps = 4000 bytes/s, so one chunk per second is real time
for i, chunk in enumerate(chunks_of(ogg_bytes, 4000)):
    reply = mcp("append_voice_stream",
                streamId=sid,
                textOffset=offset,
                text=words_for(i),          # optional, but send it if you have it
                audioBase64=b64encode(chunk).decode())
    offset = reply["textOffset"]
    assert reply["audioDuration"] > 0, "the server is not decoding this audio"
    time.sleep(1.0)

final = mcp("finish_voice_stream", streamId=sid)
print(f"posted message {final['entryId']}, {final['audioDuration']:.1f}s of speech")
```

Send the audio for a passage before the text of it, or pass `audioOffset` —
seconds into your own audio at the end of that text — so the transcript lines
up with the sound for word-level seeking. A chunk with neither is pinned to
however much audio has arrived, which is right if you are sending them together.

## The stream-capable path

A .NET client talking to the Voxt RPC API can skip the lease entirely and hand
the server the stream:

```csharp
var chats = services.GetRequiredService<IChats>();
var session = new Session(apiKey); // an API key is a session token

var entry = await chats.StreamEntry(
    session,
    ChatId.Parse(chatId),
    localId: null, // or your own recent message's LID, to rewrite it
    language: null, // or the language you are writing in, e.g. Languages.German
    RpcStream.New(TextChunks()),
    cancellationToken);

Console.WriteLine($"posted: {entry.Content}");

async IAsyncEnumerable<string> TextChunks()
{
    await foreach (var piece in SomeLlm())
        yield return piece;
}
```

One call, one result: the finished `ChatEntry`. Pacing, finalization on failure
and the maintenance checks are the same as above — you just don't have to carry
an offset.

Voice has the same shape. `IChats.StreamVoice` takes one stream of
`VoiceStreamPart`, each carrying Ogg Opus bytes, the text they cover, or both:

```csharp
var entry = await chats.StreamVoice(
    session,
    ChatId.Parse(chatId),
    repliedEntryLid: null,
    language: Languages.English,
    RpcStream.New(Parts()),
    cancellationToken);

async IAsyncEnumerable<VoiceStreamPart> Parts()
{
    await foreach (var (audio, text) in SomeProducer())
        yield return new VoiceStreamPart(audio, text, AudioOffset: null);
}
```

One stream rather than two on purpose: the order is what pins the transcript to
the sound, so the words for a passage travel with the audio that carries them.
It returns the posted message, or null if the producer sent neither audio nor
words. Everything else — the 20 ms packet rule, sending no slower than real
time — applies unchanged; only the offset bookkeeping goes away. Unlike the MCP path this one *does* accept an edit of a message
older than 15 minutes: it holds the whole stream, so it can collect the text
and apply it as one ordinary edit.

## Source

[IChats.cs](https://github.com/Actual-Chat/actual-chat/blob/main/src/dotnet/Api.Contracts/Chat/IChats.cs),
[TextEntryStreamer.cs](https://github.com/Actual-Chat/actual-chat/blob/main/src/dotnet/Chat.Service/TextEntryStreamer.cs),
[ChatEntryStreams.cs](https://github.com/Actual-Chat/actual-chat/blob/main/src/dotnet/Chat.Service/ChatEntryStreams.cs),
[ChatVoiceStreams.cs](https://github.com/Actual-Chat/actual-chat/blob/main/src/dotnet/Chat.Service/ChatVoiceStreams.cs),
[McpMessageTools.cs](https://github.com/Actual-Chat/actual-chat/blob/main/src/dotnet/Mcp/Tools/McpMessageTools.cs).

The MCP server also carries a condensed version of all of this in its
`instructions`, delivered to every client at initialize
([McpServerInstructions.cs](https://github.com/Actual-Chat/actual-chat/blob/main/src/dotnet/Mcp/Module/McpServerInstructions.cs)).
