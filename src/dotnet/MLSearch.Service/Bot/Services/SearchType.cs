namespace ActualChat.MLSearch.Bot.Services;

[Flags]
public enum SearchType
{
    None = 0,
    Public = 1,
    Private = 2,
    General = Public | Private,
}
