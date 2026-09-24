namespace ActualChat.Mcp.Module;

/// <summary>
/// What MCP hands every client at initialize. It carries what no single tool description can:
/// which of the three ways to post a message applies, and the rules that span all of them.
/// </summary>
public static class McpServerInstructions
{
    public const string Text =
        """
        Voxt is a chat where speech and text are the same message: what someone says is
        transcribed, and what someone writes can be spoken aloud. You post as yourself.

        ## Posting a message

        Four ways, in rising order of what they ask of you:

        - post_message - ordinary text, all at once.
        - start_message_stream / append_message_stream / finish_message_stream - text that
          arrives over time, e.g. as you generate it. It appears immediately and fills in live,
          the way a transcript grows while someone speaks. Someone listening to the chat hears
          it in a synthesized voice.
          If you want to be heard as well as read, write at speaking pace - a voice speaks
          about 15 characters a second, and text sent faster than that is spoken after it
          appears, by a gap that grows for as long as you outrun the voice.
        - start_voice_stream with audio only - you supply the sound. Nothing transcribes it, so
          the message carries no text.
        - start_voice_stream with audio and text - you supply both, and neither speech
          recognition nor synthesis runs. This is how you sound like yourself: your own voice,
          and a transcript that is exactly your words rather than a guess at them.

        ## Audio

        Ogg Opus, and every packet must hold one 20 ms frame - the format a live listener is
        played without re-encoding. Most encoders default to this; the flags that force it are
        `ffmpeg -c:a libopus -frame_duration 20` and `opusenc --framesize 20`. A 40 ms or 60 ms
        packet is rejected, and the whole stream with it.

        Send it in order, chunked anywhere you like - a chunk may split an Ogg page. Deliver at
        least as fast as the audio plays: someone listening hears it live, and audio that
        arrives slower than real time is silence in the middle of a sentence. Faster is fine.

        `audioDuration` in the reply is what the server actually decoded. If it stops growing
        while `audioBytes` does, your audio is not being read - check the packet size first.

        Send a clause of text when the audio carrying it is going out, not on a fixed
        cadence: a listener reads the text as it arrives and hears the audio when playback
        reaches it, so evenly spaced text drifts against unevenly sized clauses and lands
        after they have been spoken. `audioOffset` does not help here - it writes the time
        map, which drives seeking on the finished message, not live display.

        ## Offsets and resuming

        `textOffset` is the number of characters the server already has. Pass what you think it
        is; if it disagrees, nothing is written and the reply carries the real offset, so a call
        you retried or lost the answer to can resume without duplicating text. Audio has no such
        check - it is append-only, and a chunk you send twice is heard twice.

        A stream with no append for 90 seconds is finished for you with whatever arrived.
        """;
}
