namespace ActualChat.Chat;

/// <summary>
/// One piece of a voice message: Ogg Opus bytes, the text they carry, or both. One stream rather
/// than two keeps the order a listener depends on - the audio for a passage arrives with the words
/// of it, which is what pins the transcript to the sound.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record VoiceStreamPart(
    [property: DataMember(Order = 0), Key(0)] byte[]? Audio,
    [property: DataMember(Order = 1), Key(1)] string? Text,
    [property: DataMember(Order = 2), Key(2)] double? AudioOffset);
