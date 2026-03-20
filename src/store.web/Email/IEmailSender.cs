namespace store.web.Email;

public interface IEmailSender
{
    Task SendAccountConfirmationAsync(string toEmail, string userName, string confirmationLink, CancellationToken ct = default);
    Task SendCodeConfirmationAsync(string toEmail, string userName, string code, CancellationToken ct = default);
}
