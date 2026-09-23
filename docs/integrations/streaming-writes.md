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
an offset. Unlike the MCP path this one *does* accept an edit of a message
older than 15 minutes: it holds the whole stream, so it can collect the text
and apply it as one ordinary edit.

## Source

[IChats.cs](https://github.com/Actual-Chat/actual-chat/blob/main/src/dotnet/Api.Contracts/Chat/IChats.cs),
[TextEntryStreamer.cs](https://github.com/Actual-Chat/actual-chat/blob/main/src/dotnet/Chat.Service/TextEntryStreamer.cs),
[ChatEntryStreams.cs](https://github.com/Actual-Chat/actual-chat/blob/main/src/dotnet/Chat.Service/ChatEntryStreams.cs),
[McpMessageTools.cs](https://github.com/Actual-Chat/actual-chat/blob/main/src/dotnet/Mcp/Tools/McpMessageTools.cs).
