using ActualChat.Roulette;

namespace ActualChat.UI.Blazor.App.Services;

public static class SpecialProfile
{
    public static readonly Profile None = Profile.New(null!, ProfilePreferences.None);
    public static readonly Profile Loading = Profile.New(null!, ProfilePreferences.None);
}
