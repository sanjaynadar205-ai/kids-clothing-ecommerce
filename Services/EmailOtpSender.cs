using System.Net;
using System.Net.Mail;

namespace KidsWearStore.Services;

public interface IEmailOtpSender
{
    Task SendRegistrationOtpAsync(string email, string firstName, string otp, CancellationToken cancellationToken = default);
}

public sealed class EmailOtpSender : IEmailOtpSender
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailOtpSender> _logger;

    public EmailOtpSender(IConfiguration configuration, ILogger<EmailOtpSender> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SendRegistrationOtpAsync(string email, string firstName, string otp, CancellationToken cancellationToken = default)
    {
        var host = _configuration["Smtp:Host"];
        var port = _configuration.GetValue<int?>("Smtp:Port") ?? 587;
        var username = _configuration["Smtp:Username"];
        var password = _configuration["Smtp:Password"];
        var from = _configuration["Smtp:From"] ?? username;
        var enableSsl = _configuration.GetValue("Smtp:EnableSsl", true);

        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(from))
        {
            throw new InvalidOperationException("SMTP email settings are not configured.");
        }

        using var message = new MailMessage
        {
            From = new MailAddress(from, "KidsWearStore"),
            Subject = "Your KidsWearStore verification code",
            IsBodyHtml = true,
            Body = $"""
                <div style='font-family:Arial,sans-serif;max-width:560px;margin:auto;color:#202124'>
                  <h2 style='margin-bottom:8px'>Verify your KidsWearStore account</h2>
                  <p>Hello {System.Net.WebUtility.HtmlEncode(firstName)},</p>
                  <p>Use the verification code below to complete your registration:</p>
                  <div style='font-size:32px;font-weight:700;letter-spacing:8px;padding:18px 20px;background:#f7f7f8;border-radius:12px;text-align:center'>{otp}</div>
                  <p style='margin-top:20px'>This code expires in 5 minutes. If you did not request this, you can safely ignore this email.</p>
                  <p style='color:#777;font-size:12px'>KidsWearStore account security</p>
                </div>
                """
        };
        message.To.Add(email);

        using var client = new SmtpClient(host, port)
        {
            EnableSsl = enableSsl,
            Credentials = new NetworkCredential(username, password),
            DeliveryMethod = SmtpDeliveryMethod.Network
        };

        cancellationToken.ThrowIfCancellationRequested();
        await client.SendMailAsync(message);
        _logger.LogInformation("Registration OTP email sent to {Email}", email);
    }
}
