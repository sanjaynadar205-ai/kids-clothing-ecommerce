namespace KidsWearStore.Services;

public interface IEmailService
{
    Task SendVerificationOtpAsync(
        string recipientEmail,
        string recipientName,
        string otp);

    Task SendEmailAsync(
        string recipientEmail,
        string recipientName,
        string subject,
        string htmlBody,
        string? textBody = null);
}