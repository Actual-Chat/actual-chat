using ActualChat.Kvas;

namespace ActualChat.UI.Blazor.App.Services;

public record ReadPosition(ChatId ChatId, long EntryLid, string Origin = "") : IHasOrigin
{
    public static ReadPosition NewInitial(ChatId chatId) => new (chatId, -1);
    public static ReadPosition NewFullyRead(ChatId chatId) => new (chatId, long.MaxValue);

    public bool IsInitial => EntryLid == -1;
}
