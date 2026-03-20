namespace store.web.Email;

public static class EmailTemplates
{
    private const string FromName = "Archivst";

    private const string BaseStyle = @"
        body { margin: 0; padding: 0; background-color: #f4f4f5; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; }
        .wrapper { width: 100%; background-color: #f4f4f5; padding: 40px 0; }
        .container { max-width: 560px; margin: 0 auto; background-color: #ffffff; border-radius: 12px; overflow: hidden; box-shadow: 0 4px 24px rgba(0,0,0,0.07); }
        .header { background-color: #0f0f0f; padding: 32px 40px; text-align: center; }
        .header-logo { color: #ffffff; font-size: 26px; font-weight: 700; letter-spacing: 0.04em; text-decoration: none; }
        .body { padding: 40px; }
        .title { font-size: 22px; font-weight: 700; color: #0f0f0f; margin: 0 0 12px; }
        .text { font-size: 15px; color: #4b5563; line-height: 1.65; margin: 0 0 24px; }
        .cta-wrapper { text-align: center; margin: 32px 0; }
        .cta-button { display: inline-block; padding: 14px 36px; background-color: #0f0f0f; color: #ffffff !important; text-decoration: none; border-radius: 8px; font-size: 15px; font-weight: 600; letter-spacing: 0.02em; }
        .divider { border: none; border-top: 1px solid #e5e7eb; margin: 28px 0; }
        .link-fallback { font-size: 13px; color: #6b7280; word-break: break-all; }
        .code-box { display: block; text-align: center; font-size: 36px; font-weight: 700; letter-spacing: 0.3em; color: #0f0f0f; background-color: #f9fafb; border: 2px solid #e5e7eb; border-radius: 10px; padding: 20px 24px; margin: 28px 0; font-variant-numeric: tabular-nums; }
        .footer { padding: 24px 40px; background-color: #f9fafb; text-align: center; }
        .footer-text { font-size: 12px; color: #9ca3af; line-height: 1.6; margin: 0; }
    ";

    public static string BuildAccountConfirmationHtml(string userName, string confirmationLink) =>
        $@"<!DOCTYPE html>
<html lang=""en"">
<head>
  <meta charset=""UTF-8"" />
  <meta name=""viewport"" content=""width=device-width,initial-scale=1"" />
  <title>Confirm your {FromName} account</title>
  <style>{BaseStyle}</style>
</head>
<body>
  <div class=""wrapper"">
    <div class=""container"">
      <div class=""header"">
        <span class=""header-logo"">{FromName}</span>
      </div>
      <div class=""body"">
        <h1 class=""title"">Confirm your email address</h1>
        <p class=""text"">Hi {HtmlEncode(userName)},</p>
        <p class=""text"">Thanks for signing up to {FromName}. To activate your account, please confirm your email address by clicking the button below.</p>
        <div class=""cta-wrapper"">
          <a href=""{HtmlEncode(confirmationLink)}"" class=""cta-button"">Confirm email address</a>
        </div>
        <hr class=""divider"" />
        <p class=""link-fallback"">If the button doesn't work, copy and paste this link into your browser:<br />{HtmlEncode(confirmationLink)}</p>
        <p class=""text"" style=""margin-top:24px;margin-bottom:0;font-size:13px;color:#6b7280;"">This link expires in 24 hours. If you didn't create an account, you can safely ignore this email.</p>
      </div>
      <div class=""footer"">
        <p class=""footer-text"">&copy; {DateTime.UtcNow.Year} {FromName}. All rights reserved.</p>
      </div>
    </div>
  </div>
</body>
</html>";

    public static string BuildAccountConfirmationText(string userName, string confirmationLink) =>
        $"""
        Confirm your {FromName} account

        Hi {userName},

        Thanks for signing up to {FromName}. To activate your account, please confirm your email address by visiting the link below:

        {confirmationLink}

        This link expires in 24 hours. If you didn't create an account, you can safely ignore this email.

        © {DateTime.UtcNow.Year} {FromName}. All rights reserved.
        """;

    public static string BuildCodeConfirmationHtml(string userName, string code) =>
        $@"<!DOCTYPE html>
<html lang=""en"">
<head>
  <meta charset=""UTF-8"" />
  <meta name=""viewport"" content=""width=device-width,initial-scale=1"" />
  <title>Your {FromName} verification code</title>
  <style>{BaseStyle}</style>
</head>
<body>
  <div class=""wrapper"">
    <div class=""container"">
      <div class=""header"">
        <span class=""header-logo"">{FromName}</span>
      </div>
      <div class=""body"">
        <h1 class=""title"">Your verification code</h1>
        <p class=""text"">Hi {HtmlEncode(userName)},</p>
        <p class=""text"">Use the code below to complete your sign-in. This code is valid for 10 minutes.</p>
        <span class=""code-box"">{HtmlEncode(code)}</span>
        <p class=""text"" style=""font-size:13px;color:#6b7280;margin-bottom:0;"">If you didn't request this code, you can safely ignore this email. Someone may have entered your email address by mistake.</p>
      </div>
      <div class=""footer"">
        <p class=""footer-text"">&copy; {DateTime.UtcNow.Year} {FromName}. All rights reserved.</p>
      </div>
    </div>
  </div>
</body>
</html>";

    public static string BuildCodeConfirmationText(string userName, string code) =>
        $"""
        Your {FromName} verification code

        Hi {userName},

        Your verification code is:

        {code}

        This code is valid for 10 minutes.

        If you didn't request this code, you can safely ignore this email.

        © {DateTime.UtcNow.Year} {FromName}. All rights reserved.
        """;

    private static string HtmlEncode(string value) =>
        System.Net.WebUtility.HtmlEncode(value);
}
