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
        [property: Key(1)] string TextIndex,
        [property: Key(2)] double StartOffset, 
        [property: Key(3)] double Duration);
    //
    // [Serializable]
    // public class MessageVariant : Variant<ChatMessage>
    // {
    //     [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull), Newtonsoft.Json.JsonIgnore]
    //     public AudioChatMessage? Audio { get => Get<AudioChatMessage>(); init => Set(value); }
    //     [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull), Newtonsoft.Json.JsonIgnore]
    //     public ChatEntryChatMessage? Chat { get => Get<ChatEntryChatMessage>(); init => Set(value); }
    //     [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull), Newtonsoft.Json.JsonIgnore]
    //     public TranscriptChatMessage? Transcript { get => Get<TranscriptChatMessage>(); init => Set(value); }
    //     [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull), Newtonsoft.Json.JsonIgnore]
    //     public ChatMessage? Other { get => Get<ChatMessage>(); init => Set(value); }
    //
    //     [JsonConstructor]
    //     public MessageVariant() { }
    //     [Newtonsoft.Json.JsonConstructor]
    //     public MessageVariant(ChatMessage? value) : base(value) { }
    // }
}