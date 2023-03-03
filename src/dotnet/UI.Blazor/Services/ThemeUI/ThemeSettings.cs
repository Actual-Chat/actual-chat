using ActualChat.Kvas;

namespace ActualChat.UI.Blazor.Services;

public enum Theme { Light = 0, Dark }

[DataContract]
public sealed record ThemeSettings(
    [property: DataMember] Theme Theme,
    [property: DataMember] string Origin = ""
    ) : IHasOrigin;
