
namespace ActualChat.MLSearch.Bot.Services;

[Flags]
public enum UserIntent
{
    None,
    PublicSearch = SearchType.Public,
    PrivateSearch = SearchType.Private,
    GeneralSearch = SearchType.General,
    Reset = 0x100,
}

internal static class UserIntentExtensions
{
    public static bool IsReset(this UserIntent intent)
        => intent == UserIntent.Reset;

    public static bool IsSearchType(this UserIntent intent, out SearchType searchType)
    {
        searchType = SearchType.None;
        if (intent == UserIntent.PublicSearch || intent == UserIntent.PrivateSearch || intent == UserIntent.GeneralSearch) {
            searchType = (SearchType)intent;
            return true;
        }
        return false;
    }
}
