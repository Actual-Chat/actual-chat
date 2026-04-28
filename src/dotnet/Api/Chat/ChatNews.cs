using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

[DataContract, MessagePackObject]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed partial record ChatNews(
    [property: DataMember, Key(0)] Range<long> TextEntryLidRange,
    [property: DataMember, Key(1)] ChatEntry? LastTextEntry = null);
