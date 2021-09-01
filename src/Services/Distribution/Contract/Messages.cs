using System;
using System.Text.Json.Serialization;
using ActualChat.Audio;
using MessagePack;
using Stl.Serialization;
using Stl.Text;

namespace ActualChat.Distribution
{
    [MessagePackObject]
    public readonly struct RecordingId
    {
        public RecordingId(string value) => Value = value;

        [Key(0)]
        public string Value { get; init; }

        public static implicit operator RecordingId(string value) => new(value);
        public static implicit operator string(RecordingId id) => id.Value;
    }
    
    [MessagePackObject]
    public record AudioRecordingConfiguration(
        [property: Key(0)] AudioFormat Format,
        [property: Key(1)] string Language,
        [property: Key(2)] double ClientStartOffset);

    [MessagePackObject]
    public record AudioRecording(
        [property: Key(0)] RecordingId Id,
        [property: Key(1)] AudioRecordingConfiguration Configuration);
    
    [MessagePackObject]
    public record AudioMessage([property: MessagePack.Key(0)] byte[] Chunk);
    
    [MessagePackObject]
    public record AudioRecordMessage(
        [property: Key(0)] int Index,
        [property: Key(1)] double ClientEndOffset,
        [property: Key(2)] byte[] Chunk);
    
    [MessagePackObject]
    public record VideoMessage([property: MessagePack.Key(0)] byte[] Chunk);

    [MessagePackObject]
    public record TranscriptMessage(
        [property: Key(0)] string Text, 
        [property: Key(1)] int TextIndex,
        [property: Key(2)] double StartOffset, 
        [property: Key(3)] double Duration);

}