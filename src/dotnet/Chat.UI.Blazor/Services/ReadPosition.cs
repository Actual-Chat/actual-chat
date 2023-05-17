using ActualChat.Kvas;

namespace ActualChat.Chat.UI.Blazor.Services;

public record ReadPosition(ChatId ChatId, long EntryLid, string Origin = "") : IHasOrigin
{
    public static readonly ReadPosition None = new ReadPosition(ChatId.None, 0, "");

    public static ReadPosition GetInitial(ChatId chatId)
        => new ReadPosition(chatId, -1, "");

    public bool IsInitial => ChatId != ChatId.None && EntryLid == -1;
}
