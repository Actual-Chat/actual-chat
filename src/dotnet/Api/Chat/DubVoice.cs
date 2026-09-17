namespace ActualChat.Chat;

/// <summary>
/// A stock TTS voice a speaker can pick to be dubbed with; every voice speaks every dub language.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record DubVoice(
    [property: DataMember, Key(0)] string Id
) : IHasId<string>
{
    [DataMember, Key(1)] public string Description { get; init; } = "";
    [DataMember, Key(2)] public string Gender { get; init; } = "";
    [DataMember, Key(3)] public string Age { get; init; } = "";
    [DataMember, Key(4)] public string Accent { get; init; } = "";
    [DataMember, Key(5)] public ApiArray<string> UseCase { get; init; }
    [DataMember, Key(6)] public ApiArray<string> Style { get; init; }
}
