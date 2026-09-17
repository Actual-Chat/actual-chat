using ActualChat.Kvas;
using ActualChat.Media;

namespace ActualChat.Users;

/// <summary>
/// The image style the user last picked, so the generator offers it again next time.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record UserImageStyleSettings : StoredSettings, IHasOrigin
{
    public const string KvasKey = nameof(UserImageStyleSettings);

    [DataMember, Key(0)] public ImageStyle Style { get; init; } = ImageStyle.Default;
    [DataMember, Key(1)] public string Origin { get; init; } = "";
}
