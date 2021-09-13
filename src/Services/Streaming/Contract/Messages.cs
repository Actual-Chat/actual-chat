using ActualChat.Audio;
using MessagePack;

namespace ActualChat.Streaming
{
    public interface IRecording
    { }
    
    public interface IRecordingConfiguration
    { }
    
    public interface IMessage
    { }

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
    public readonly struct StreamId
    {
        public StreamId(RecordingId id, int index) : this ($"{id.Value}-{index:D4}")
        { }

        private StreamId(string value) => Value = value;

        [Key(0)]
        public string Value { get; init; }

        public static implicit operator StreamId(string value) => new(value);
        public static implicit operator string(StreamId id) => id.Value;
    }
    
    [MessagePackObject]
    public record AudioRecordingConfiguration(
        [property: Key(0)] AudioFormat Format,
        [property: Key(1)] string Language,
        [property: Key(2)] double ClientStartOffset): IRecordingConfiguration;

    [MessagePackObject]
    public record AudioRecording(
        [property: Key(0)] RecordingId Id,
        [property: Key(1)] AudioRecordingConfiguration Configuration) : IRecording;
    
    [MessagePackObject]
    public record VideoRecordingConfiguration(
        [property: Key(0)] double ClientStartOffset): IRecordingConfiguration;

    [MessagePackObject]
    public record VideoRecording(
        [property: Key(0)] RecordingId Id,
        [property: Key(1)] VideoRecordingConfiguration Configuration): IRecording;
    
    [MessagePackObject]
    public record BlobMessage(
        [property: Key(0)] int Index,
        [property: Key(2)] byte[] Chunk) : IMessage;
    
    [MessagePackObject]
    public record TranscriptMessage(
        [property: Key(0)] string Text, 
        [property: Key(1)] int TextIndex,
        [property: Key(2)] double StartOffset, 
        [property: Key(3)] double Duration) : IMessage;

}