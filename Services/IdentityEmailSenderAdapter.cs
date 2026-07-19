using MedLoop.NextGen.Models;
using Microsoft.AspNetCore.Identity;

namespace MedLoop.NextGen.Services;

// Without this registered, AddIdentityApiEndpoints falls back to a no-op
// email sender internally — account confirmation and password-reset
// emails would silently never be sent, with no error anywhere. This
// adapts our own IEmailSender (Gmail-backed) to the framework's expected
// IEmailSender<TUser> shape.
public class IdentityEmailSenderAdapter : IEmailSender<ApplicationUser>
{
    private readonly IEmailSender _emailSender;

    public IdentityEmailSenderAdapter(IEmailSender emailSender)
    {
        _emailSender = emailSender;
    }

    public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink) =>
        _emailSender.SendAsync(email, "Confirm your MedLoop account",
            $"Please confirm your account by <a href=\"{confirmationLink}\">clicking here</a>.");

    public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink) =>
        _emailSender.SendAsync(email, "Reset your MedLoop password",
            $"Reset your password by <a href=\"{resetLink}\">clicking here</a>.");

    public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode) =>
        _emailSender.SendAsync(email, "Your MedLoop password reset code",
            $"Your password reset code is: <strong>{resetCode}</strong>");
}
