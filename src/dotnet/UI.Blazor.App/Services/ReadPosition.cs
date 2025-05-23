using ActualChat.Kvas;

namespace ActualChat.UI.Blazor.App.Services;

public record ReadPosition(ChatId ChatId, long EntryLid, string Origin = "") : IHasOrigin
{
    public static ReadPosition GetInitial(ChatId chatId)
        => new (chatId, -1, "");

    public bool IsInitial => EntryLid == -1;
}
