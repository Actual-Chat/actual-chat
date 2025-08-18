namespace ActualChat.Users.Phone;

public interface ITextMessageSender
{
    Task Send(ActualChat.Phone phone, string text);
}
