namespace FMS.Application.Auth;

public interface IEmailService
{
    Task SendPasswordResetEmailAsync(string email, string resetLink);
}
