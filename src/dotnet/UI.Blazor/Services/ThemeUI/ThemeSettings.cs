using ActualChat.Kvas;

namespace ActualChat.UI.Blazor.Services;

[DataContract]
public sealed record ThemeSettings(
    [property: DataMember] Theme Theme,
    [property: DataMember] string Origin = ""
    ) : IHasOrigin;
