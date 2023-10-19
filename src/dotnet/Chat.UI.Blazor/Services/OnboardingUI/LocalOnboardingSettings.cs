using ActualChat.Kvas;
using MemoryPack;

namespace ActualChat.Chat.UI.Blazor.Services;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record LocalOnboardingSettings
{
    public const string KvasKey = nameof(LocalOnboardingSettings);

    [DataMember, MemoryPackOrder(0)] public bool IsPermissionsStepCompleted { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool HasUncompletedSteps
        => this is not {
            IsPermissionsStepCompleted: true,
        };
}
