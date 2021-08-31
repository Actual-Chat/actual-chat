using System;
using System.Text.Json.Serialization;
using MessagePack;
using Stl.Serialization;
using Stl.Text;

namespace ActualChat.Distribution
{
    [MessagePackObject]
    public record AudioMessage([property: MessagePack.Key(0)] byte[] Chunk);

    [MessagePackObject]
    public record VideoMessage([property: MessagePack.Key(0)] byte[] Chunk);

    [MessagePackObject]
    public record TranscriptMessage(
        [property: Key(0)] string Text, 
        [property: Key(1)] int TextIndex,
        [property: Key(2)] double StartOffset, 
        [property: Key(3)] double Duration);

}