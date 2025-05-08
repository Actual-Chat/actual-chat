using ActualChat.Roulette;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

public static class SpecialProfile
{
    public static readonly Profile None = Profile.New(SpecialAvatar.None, ProfilePreferences.None);
    public static readonly Profile Loading = Profile.New(SpecialAvatar.Loading, ProfilePreferences.None);
}
