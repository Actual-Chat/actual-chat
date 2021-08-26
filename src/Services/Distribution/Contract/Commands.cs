using System;
using System.Text.Json.Serialization;
using Stl.Serialization;

namespace ActualChat.Distribution
{
    public record ChatMessage
    {
    }

    public record AudioChatMessage(byte[] Chunk) : ChatMessage;
    
    public record ChatEntryChatMessage() : ChatMessage;
    
    public record TranscriptChatMessage() : ChatMessage;

    [Serializable]
    public class MessageVariant : Variant<ChatMessage>
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull), Newtonsoft.Json.JsonIgnore]
        public AudioChatMessage? Audio { get => Get<AudioChatMessage>(); init => Set(value); }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull), Newtonsoft.Json.JsonIgnore]
        public ChatEntryChatMessage? Chat { get => Get<ChatEntryChatMessage>(); init => Set(value); }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull), Newtonsoft.Json.JsonIgnore]
        public TranscriptChatMessage? Transcript { get => Get<TranscriptChatMessage>(); init => Set(value); }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull), Newtonsoft.Json.JsonIgnore]
        public ChatMessage? Other { get => Get<ChatMessage>(); init => Set(value); }

        [JsonConstructor]
        public MessageVariant() { }
        [Newtonsoft.Json.JsonConstructor]
        public MessageVariant(ChatMessage? value) : base(value) { }
    }
}