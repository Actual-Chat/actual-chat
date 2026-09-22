using ActualChat.Users.Email;

namespace ActualChat.Users.UnitTests;

public class EmailSenderTest
{
    [Fact]
    public void ShouldAddOneClickUnsubscribeHeaders()
    {
        // act
        var message = EmailSender.NewMessage(
            "noreply@voxt.ai", "", "alice@example.com", "Digest", "<p>hi</p>",
            "https://voxt.ai/emails/digest/token/unsubscribe");

        // assert
        message.Headers["List-Unsubscribe"].Should().Be("<https://voxt.ai/emails/digest/token/unsubscribe>");
        message.Headers["List-Unsubscribe-Post"].Should().Be("List-Unsubscribe=One-Click");
    }

    [Fact]
    public void ShouldNotAddUnsubscribeHeadersWithoutUrl()
    {
        // act
        var message = EmailSender.NewMessage(
            "noreply@voxt.ai", "", "alice@example.com", "Code", "<p>1234</p>", null);

        // assert
        message.Headers.Contains("List-Unsubscribe").Should().BeFalse();
        message.Headers.Contains("List-Unsubscribe-Post").Should().BeFalse();
    }
}
