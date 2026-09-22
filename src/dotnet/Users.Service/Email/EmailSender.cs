using ActualChat.Users.Module;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace ActualChat.Users.Email;

public interface IEmailSender
{
    Task Send(
        string name, string email, string subject, string html,
        string? unsubscribeUrl, CancellationToken cancellationToken);
}

public sealed class EmailSender(IServiceProvider services) : IEmailSender
{
    private UsersSettings Settings => field ??= services.GetRequiredService<UsersSettings>();
    private ILogger Log { get; } = services.LogFor<EmailSender>();

    public async Task Send(
        string name, string email, string subject, string html,
        string? unsubscribeUrl, CancellationToken cancellationToken)
    {
        if (!Settings.IsSmtpEnabled) {
            Log.LogInformation("Email to {Email}: {Subject}", email, subject);
            return;
        }

        var message = NewMessage(Settings.SmtpFrom, name, email, subject, html, unsubscribeUrl);
        using var client = new SmtpClient();
        await client
            .ConnectAsync(
                Settings.SmtpHost,
                Settings.SmtpPort,
                Settings.SmtpUseSsl
                    ? SecureSocketOptions.StartTls
                    : SecureSocketOptions.Auto,
                cancellationToken)
            .ConfigureAwait(false);
        await client.AuthenticateAsync(
                Settings.SmtpLogin,
                Settings.SmtpPassword,
                cancellationToken)
            .ConfigureAwait(false);
        await client.SendAsync(message, cancellationToken).ConfigureAwait(false);
        await client.DisconnectAsync(true, cancellationToken).ConfigureAwait(false);
    }

    internal static MimeMessage NewMessage(
        string from, string name, string email, string subject, string html, string? unsubscribeUrl)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("", from));
        message.To.Add(new MailboxAddress(name, email));
        message.Subject = subject;
        message.Body = new TextPart("html") {
            Text = html,
        };
        if (unsubscribeUrl is not null) {
            // RFC 8058: lets Gmail & co. show their own Unsubscribe button and honor it with a POST
            message.Headers.Add("List-Unsubscribe", $"<{unsubscribeUrl}>");
            message.Headers.Add("List-Unsubscribe-Post", "List-Unsubscribe=One-Click");
        }
        return message;
    }
}
