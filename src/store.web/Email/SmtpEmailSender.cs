using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace store.web.Email;

public sealed class SmtpEmailSender : IEmailSender
{
    private const string FromName = "Archivst";

    private readonly SmtpSettings _settings;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(SmtpSettings settings, ILogger<SmtpEmailSender> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public Task SendAccountConfirmationAsync(string toEmail, string userName, string confirmationLink, CancellationToken ct = default)
    {
        var message = BuildMessage(
            toEmail,
            subject: $"Confirm your {FromName} account",
            htmlBody: EmailTemplates.BuildAccountConfirmationHtml(userName, confirmationLink),
            textBody: EmailTemplates.BuildAccountConfirmationText(userName, confirmationLink));

        return SendAsync(message, ct);
    }

    public Task SendCodeConfirmationAsync(string toEmail, string userName, string code, CancellationToken ct = default)
    {
        var message = BuildMessage(
            toEmail,
            subject: $"Your {FromName} verification code",
            htmlBody: EmailTemplates.BuildCodeConfirmationHtml(userName, code),
            textBody: EmailTemplates.BuildCodeConfirmationText(userName, code));

        return SendAsync(message, ct);
    }

    private MimeMessage BuildMessage(string toEmail, string subject, string htmlBody, string textBody)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(FromName, _settings.FromAddress));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;

        var bodyBuilder = new BodyBuilder
        {
            HtmlBody = htmlBody,
            TextBody = textBody,
        };

        message.Body = bodyBuilder.ToMessageBody();
        return message;
    }

    private async Task SendAsync(MimeMessage message, CancellationToken ct)
    {
        using var client = new SmtpClient();
        try
        {
            await client.ConnectAsync(_settings.Host, _settings.Port, _settings.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable, ct);

            if (!string.IsNullOrEmpty(_settings.UserName))
                await client.AuthenticateAsync(_settings.UserName, _settings.Password, ct);

            await client.SendAsync(message, ct);
            await client.DisconnectAsync(quit: true, ct);

            _logger.LogInformation("Email sent to {Recipient} with subject '{Subject}'", message.To, message.Subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {Recipient}", message.To);
            throw;
        }
    }
}
