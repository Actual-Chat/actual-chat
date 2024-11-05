namespace ActualChat.MLSearch.Bot.Services;

[Flags]
internal enum SearchType
{
    None = 0,
    Public = 1,
    Private = 2,
    General = Public | Private,
}
