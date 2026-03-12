using ActualChat.Kvas;

namespace ActualChat.UI.Blazor.App.Pages.Test;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public sealed partial record UserTranscodingTestSettings : IHasKvasKey<UserTranscodingTestSettings>
{
    [DataMember, MemoryPackOrder(0)] public string FileName { get; init; } = "";
    [DataMember, MemoryPackOrder(1)] public UploadId? UploadId { get; init; }
    [DataMember, MemoryPackOrder(2)] public MediaId? MediaId { get; init; }
}

public static class KvasExt
{
    public static KvasAccessor<UserTranscodingTestSettings> UserTranscodingTestSettings(this IKvas<Account> kvas)
        => kvas.For<UserTranscodingTestSettings>();
}
