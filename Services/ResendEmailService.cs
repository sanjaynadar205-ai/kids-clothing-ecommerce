using Resend;

namespace KidsWearStore.Services;

public class ResendEmailService : IEmailService
{
    private readonly IResend _resend;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ResendEmailService> _logger;

    public ResendEmailService(
        IResend resend,
        IConfiguration configuration,
        ILogger<ResendEmailService> logger)
    {
        _resend = resend;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SendVerificationOtpAsync(
        string recipientEmail,
        string recipientName,
        string otp)
    {
        var fromEmail =
            _configuration["Resend:FromEmail"];

        var fromName =
            _configuration["Resend:FromName"]
            ?? "CrisKidsWear";

        if (string.IsNullOrWhiteSpace(fromEmail))
        {
            throw new InvalidOperationException(
                "Resend:FromEmail is not configured.");
        }

        var safeName =
            string.IsNullOrWhiteSpace(recipientName)
                ? "Customer"
                : recipientName.Trim();

        var message = new EmailMessage
        {
            From = $"{fromName} <{fromEmail}>",
            Subject = "Verify your CrisKidsWear account",
            HtmlBody = BuildVerificationEmail(
                safeName,
                otp)
        };

        message.To.Add(recipientEmail);

        try
        {
            var response =
                await _resend.EmailSendAsync(message);

            _logger.LogInformation(
                "Verification email sent successfully to {Email}. Resend email ID: {EmailId}",
                MaskEmail(recipientEmail),
                response.Content);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Resend failed to send verification email to {Email}.",
                MaskEmail(recipientEmail));

            throw;
        }
    }

    public async Task SendEmailAsync(
        string recipientEmail,
        string recipientName,
        string subject,
        string htmlBody,
        string? textBody = null)
    {
        var fromEmail =
            _configuration["Resend:FromEmail"];

        var fromName =
            _configuration["Resend:FromName"]
            ?? "CrisKidsWear";

        if (string.IsNullOrWhiteSpace(fromEmail))
        {
            throw new InvalidOperationException(
                "Resend:FromEmail is not configured.");
        }

        var message = new EmailMessage
        {
            From = $"{fromName} <{fromEmail}>",
            Subject = subject,
            HtmlBody = htmlBody
        };

        message.To.Add(recipientEmail);

        try
        {
            var response =
                await _resend.EmailSendAsync(message);

            _logger.LogInformation(
                "Email sent successfully to {Email}. Resend email ID: {EmailId}",
                MaskEmail(recipientEmail),
                response.Content);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Resend failed to send email to {Email}.",
                MaskEmail(recipientEmail));

            throw;
        }
    }

    private static string BuildVerificationEmail(
        string name,
        string otp)
    {
        return $"""
        <!DOCTYPE html>
        <html>
        <head>
            <meta charset="UTF-8">
            <meta name="viewport"
                  content="width=device-width, initial-scale=1.0">

            <title>Verify your CrisKidsWear account</title>
        </head>

        <body style="
            margin:0;
            padding:0;
            background:#f5f6f8;
            font-family:Arial,Helvetica,sans-serif;
            color:#222;
        ">

            <div style="
                max-width:600px;
                margin:40px auto;
                background:#ffffff;
                border:1px solid #e5e7eb;
                border-radius:12px;
                overflow:hidden;
            ">

                <div style="
                    background:#111827;
                    padding:28px 30px;
                    text-align:center;
                ">
                    <h1 style="
                        margin:0;
                        color:#ffffff;
                        font-size:28px;
                        font-weight:700;
                    ">
                        CrisKidsWear
                    </h1>
                </div>

                <div style="
                    padding:35px 30px;
                ">

                    <h2 style="
                        margin:0 0 18px;
                        font-size:24px;
                        color:#111827;
                    ">
                        Verify your email address
                    </h2>

                    <p style="
                        margin:0 0 14px;
                        font-size:16px;
                        line-height:1.6;
                    ">
                        Hello {System.Net.WebUtility.HtmlEncode(name)},
                    </p>

                    <p style="
                        margin:0 0 24px;
                        font-size:16px;
                        line-height:1.6;
                        color:#4b5563;
                    ">
                        Thank you for registering with CrisKidsWear.
                        Use the verification code below to complete
                        your account registration.
                    </p>

                    <div style="
                        text-align:center;
                        margin:30px 0;
                    ">

                        <div style="
                            display:inline-block;
                            padding:18px 30px;
                            background:#f3f4f6;
                            border:1px solid #d1d5db;
                            border-radius:10px;
                            font-size:34px;
                            font-weight:700;
                            letter-spacing:8px;
                            color:#111827;
                        ">
                            {System.Net.WebUtility.HtmlEncode(otp)}
                        </div>

                    </div>

                    <p style="
                        margin:0 0 10px;
                        font-size:14px;
                        line-height:1.6;
                        color:#6b7280;
                    ">
                        This verification code expires in
                        <strong>5 minutes</strong>.
                    </p>

                    <p style="
                        margin:24px 0 0;
                        font-size:14px;
                        line-height:1.6;
                        color:#6b7280;
                    ">
                        If you did not create a CrisKidsWear account,
                        you can safely ignore this email.
                    </p>

                </div>

                <div style="
                    padding:20px 30px;
                    background:#f9fafb;
                    border-top:1px solid #e5e7eb;
                    text-align:center;
                ">

                    <p style="
                        margin:0;
                        font-size:12px;
                        color:#9ca3af;
                    ">
                        CrisKidsWear
                    </p>

                </div>

            </div>

        </body>
        </html>
        """;
    }

    private static string MaskEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return "***";
        }

        var atIndex = email.IndexOf('@');

        if (atIndex <= 0)
        {
            return "***";
        }

        var localPart = email[..atIndex];

        if (localPart.Length == 1)
        {
            return $"*@{email[(atIndex + 1)..]}";
        }

        var visibleCharacters =
            Math.Min(2, localPart.Length);

        return
            $"{localPart[..visibleCharacters]}***@{email[(atIndex + 1)..]}";
    }
}