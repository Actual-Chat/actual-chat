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
          If you want to be heard as well as read, watch `speechBacklog` in the reply: the
          seconds of speech a listener still has to hear before reaching what you just wrote.
          Slow down while it grows; ignore it while it is null, which means nobody is
          listening. Failing that, write at speaking pace for the language you declared -
          roughly 15 characters a second for English, 17 for Russian, 6 for Japanese, 4 for
          Mandarin - but the backlog is the real answer, because how fast a voice speaks
          depends on the language, the voice and the provider.
          To start over, append a line holding only `<!--reset-->`: it and everything above it
          is dropped from what readers see, and the text after it is the message.
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

        ## Importing history

        Owners of a group chat or a Place can import history - e.g. a Telegram, WhatsApp or
        Slack export - with the original authors and dates. Every author must consent first.
        Imported dates must be later than everything already in the chat, system messages such
        as "joined" included; in a group chat that means after its members joined. Only
        messages still in the chat count, so removing some moves that limit back: during an
        import, wipe_history removes the chat's history - it works only then - and
        remove_message removes one message.
        Starting an import stops posting for every member, the owner included, until it ends;
        a Place-wide import stops every chat of the Place. So confirm with the user before
        start_import, and end the import as soon as you are done. The order:

        1. get_import - check that no import runs yet.
        2. start_import with `confirm: true`.
        3. Optionally, wipe_history - e.g. to replace an earlier, partial import.
        4. Remind the members in `pending` with post_message in their `peerChatId` - direct
           chats stay open during the import - and poll get_import until enough have consented.
           The owner consents with consent_to_import like everyone else.
        5. Upload attachments: begin_import_upload, append_upload, finish_import_upload, or
           import_upload_from_url - with the same chatId you will pass to import_messages,
           which for a Place-wide import is a Place chat, not the root.
        6. import_messages in batches, oldest first.
        7. end_import.
        """;
}
