using System.Text;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;

namespace MedLoop.NextGen.Services;

// Sends mail via the Gmail API using a Google Workspace service account
// with domain-wide delegation, impersonating a real mailbox as the sender
// — no SMTP, no password, ever. This mirrors the fix already made to the
// legacy PharmacyWebapp's EmailService, which originally used raw SMTP
// with an App Password — the same class of credential that leaked into
// that repo's git history.
//
// Setup (same as PharmacyWebapp's equivalent fix, done once per environment,
// not something this code can do for you):
//   1. Enable the Gmail API on the GCP project holding your service account.
//   2. In Google Workspace Admin Console -> Security -> API Controls ->
//      Domain-wide Delegation, authorize that service account's numeric
//      Client ID for scope https://www.googleapis.com/auth/gmail.send.
//   3. Set GOOGLE_APPLICATION_CREDENTIALS to the service account JSON key
//      path, and Email:SenderAddress (appsettings) to a real mailbox in
//      that Workspace domain.
public class GmailEmailSender : IEmailSender
{
    private static readonly string[] Scopes = { GmailService.Scope.GmailSend };

    private readonly IConfiguration _configuration;

    public GmailEmailSender(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        // Validated here, not in the constructor: this service is
        // Singleton (required so ASP.NET Core Identity's MapIdentityApi can
        // resolve IEmailSender<TUser> from the root container at startup —
        // see Program.cs), which means the constructor runs once, eagerly,
        // during app startup regardless of whether anyone ever sends an
        // email. A constructor that throws on missing config was found (by
        // actually running the app) to take down the entire application at
        // boot instead of just failing the first real send — moving the
        // check here means a misconfigured environment can still start and
        // serve every request that doesn't need email, and fails clearly
        // only when a send is actually attempted.
        var senderAddress = _configuration["Email:SenderAddress"];
        if (string.IsNullOrWhiteSpace(senderAddress))
        {
            throw new InvalidOperationException("Email:SenderAddress is not configured.");
        }

        var credential = (await GoogleCredential.GetApplicationDefaultAsync(cancellationToken))
            .CreateScoped(Scopes)
            .CreateWithUser(senderAddress);

        using var gmailService = new GmailService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "MedLoop NextGen"
        });

        var message = new Message
        {
            Raw = BuildRawMessage(senderAddress, toEmail, subject, htmlBody)
        };

        await gmailService.Users.Messages.Send(message, "me").ExecuteAsync(cancellationToken);
    }

    private static string BuildRawMessage(string from, string to, string subject, string htmlBody)
    {
        var mime = new StringBuilder();
        mime.Append("From: ").Append(from).Append("\r\n");
        mime.Append("To: ").Append(to).Append("\r\n");
        mime.Append("Subject: ").Append(EncodeHeader(subject)).Append("\r\n");
        mime.Append("MIME-Version: 1.0\r\n");
        mime.Append("Content-Type: text/html; charset=UTF-8\r\n\r\n");
        mime.Append(htmlBody);

        var bytes = Encoding.UTF8.GetBytes(mime.ToString());
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    private static string EncodeHeader(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return "=?UTF-8?B?" + Convert.ToBase64String(bytes) + "?=";
    }
}
