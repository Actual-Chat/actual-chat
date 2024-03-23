using ActualChat.Users.Module;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace ActualChat.Users.Email;

public interface IEmailSender
{
    Task Send(string name, string email, string subject, string html, CancellationToken cancellationToken);
}

public class EmailSender(IServiceProvider services) : IEmailSender
{
    private UsersSettings? _settings;
    private UsersSettings Settings => _settings ??= services.GetRequiredService<UsersSettings>();

    public async Task Send(string name, string email, string subject, string html, CancellationToken cancellationToken)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("", Settings.SmtpFrom));
        message.To.Add(new MailboxAddress(name, email));
        message.Subject = subject;
        message.Body = new TextPart("html") {
            Text = html,
        };

        using var client = new SmtpClient();
        await client
            .ConnectAsync(Settings.SmtpHost, Settings.SmtpPort, SecureSocketOptions.StartTls, cancellationToken)
            .ConfigureAwait(false);
        await client.AuthenticateAsync(Settings.SmtpLogin, Settings.SmtpPassword, cancellationToken).ConfigureAwait(false);
        await client.SendAsync(message, cancellationToken).ConfigureAwait(false);
        await client.DisconnectAsync(true, cancellationToken).ConfigureAwait(false);
    }
}
