using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KidsWearStore.Data;
using KidsWearStore.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KidsWearStore.Controllers;

public class AccountController : Controller
{
    private const string PendingRegistrationSessionKey =
        "KidsWear.PendingRegistration";

    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IConfiguration _configuration;

    public AccountController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IConfiguration configuration)
    {
        _context = context;
        _userManager = userManager;
        _signInManager = signInManager;
        _configuration = configuration;
    }

    // =========================================================
    // LOGIN
    // =========================================================

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        return View(
            new LoginViewModel
            {
                ReturnUrl = returnUrl
            });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(
        LoginViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var email = model.Email.Trim();

        var user =
            await _userManager.FindByEmailAsync(email);

        if (user == null)
        {
            ModelState.AddModelError(
                string.Empty,
                "Invalid email or password.");

            return View(model);
        }

        // -----------------------------------------------------
        // Check password first.
        // -----------------------------------------------------

        var passwordValid =
            await _userManager.CheckPasswordAsync(
                user,
                model.Password);

        if (!passwordValid)
        {
            ModelState.AddModelError(
                string.Empty,
                "Invalid email or password.");

            return View(model);
        }

        // -----------------------------------------------------
        // EMAIL VERIFICATION IS REQUIRED
        //
        // This prevents accounts from being used before the
        // 6-digit OTP verification is completed.
        // -----------------------------------------------------

        if (!user.EmailConfirmed)
        {
            ModelState.AddModelError(
                string.Empty,
                "Please verify your email address before signing in.");

            // Prepare a fresh OTP so the user can immediately
            // continue verification.
            try
            {
                await SendVerificationOtpAsync(user);

                TempData["Info"] =
                    "Your email is not verified. " +
                    "A new verification code has been sent to your email address.";

                return RedirectToAction(
                    nameof(VerifyOtp));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    "==================================================");

                Console.Error.WriteLine(
                    "LOGIN OTP RESEND ERROR");

                Console.Error.WriteLine(
                    ex.ToString());

                Console.Error.WriteLine(
                    "==================================================");

                TempData["Error"] =
                    "Your email is not verified and we could not send " +
                    "a new verification code right now. Please try again.";

                return RedirectToAction(
                    nameof(Login),
                    new
                    {
                        returnUrl = model.ReturnUrl
                    });
            }
        }

        // -----------------------------------------------------
        // Verified user can sign in.
        // -----------------------------------------------------

        await _signInManager.SignInAsync(
            user,
            model.RememberMe);

        if (!string.IsNullOrWhiteSpace(model.ReturnUrl) &&
            Url.IsLocalUrl(model.ReturnUrl))
        {
            return Redirect(model.ReturnUrl);
        }

        return RedirectToAction(
            "Index",
            "Home");
    }


    // =========================================================
    // REGISTER
    // =========================================================

    [HttpGet]
    public IActionResult Register()
    {
        return View(
            new RegisterViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(
        RegisterViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var email = model.Email.Trim();

        // -----------------------------------------------------
        // Check whether this email already exists.
        // -----------------------------------------------------

        var existingUser =
            await _userManager.FindByEmailAsync(email);

        if (existingUser != null)
        {
            // -------------------------------------------------
            // EXISTING BUT UNVERIFIED ACCOUNT
            //
            // Allow the user to continue the verification
            // process instead of showing "account already exists".
            // -------------------------------------------------

            if (!existingUser.EmailConfirmed)
            {
                try
                {
                    await SendVerificationOtpAsync(
                        existingUser);

                    TempData["Info"] =
                        "This account is awaiting email verification. " +
                        "A new verification code has been sent.";

                    return RedirectToAction(
                        nameof(VerifyOtp));
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(
                        "==================================================");

                    Console.Error.WriteLine(
                        "EXISTING USER OTP ERROR");

                    Console.Error.WriteLine(
                        ex.ToString());

                    Console.Error.WriteLine(
                        "==================================================");

                    ModelState.AddModelError(
                        nameof(model.Email),
                        "This account is not verified yet, but we could not " +
                        "send a verification code. Please try again later.");

                    return View(model);
                }
            }

            // -------------------------------------------------
            // VERIFIED ACCOUNT ALREADY EXISTS
            // -------------------------------------------------

            ModelState.AddModelError(
                nameof(model.Email),
                "An account with this email already exists. Please sign in.");

            return View(model);
        }


        // -----------------------------------------------------
        // Create a NEW unverified user.
        // -----------------------------------------------------

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,

            // IMPORTANT:
            // New accounts remain unverified until OTP succeeds.
            EmailConfirmed = false,

            FirstName = model.FirstName.Trim(),
            LastName = model.LastName.Trim(),

            PhoneNumber =
                string.IsNullOrWhiteSpace(model.PhoneNumber)
                    ? null
                    : model.PhoneNumber.Trim()
        };

        var createResult =
            await _userManager.CreateAsync(
                user,
                model.Password);

        if (!createResult.Succeeded)
        {
            foreach (var error in createResult.Errors)
            {
                ModelState.AddModelError(
                    string.Empty,
                    error.Description);
            }

            return View(model);
        }


        // -----------------------------------------------------
        // SEND OTP
        //
        // If the email cannot be sent, delete the newly-created
        // account so the user does not get stuck with an account
        // that cannot be verified.
        // -----------------------------------------------------

        try
        {
            await SendVerificationOtpAsync(user);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                "==================================================");

            Console.Error.WriteLine(
                "REGISTRATION OTP SEND ERROR");

            Console.Error.WriteLine(
                ex.ToString());

            Console.Error.WriteLine(
                "==================================================");

            // Remove the account because registration did not
            // successfully complete the verification step.
            var deleteResult =
                await _userManager.DeleteAsync(user);

            if (!deleteResult.Succeeded)
            {
                foreach (var error in deleteResult.Errors)
                {
                    Console.Error.WriteLine(
                        $"Unable to remove unverified user: " +
                        $"{error.Description}");
                }
            }

            ModelState.AddModelError(
                string.Empty,
                "We could not send the verification email. " +
                "Your registration was not completed. " +
                "Please try again.");

            return View(model);
        }


        // -----------------------------------------------------
        // OTP successfully sent.
        // -----------------------------------------------------

        TempData["Info"] =
            "A 6-digit verification code has been sent to your email address.";

        return RedirectToAction(
            nameof(VerifyOtp));
    }


    // =========================================================
    // VERIFY OTP - GET
    // =========================================================

    [HttpGet]
    public IActionResult VerifyOtp()
    {
        var pending =
            GetPendingRegistration();

        if (pending == null)
        {
            TempData["Error"] =
                "Your verification session has expired. " +
                "Please register again.";

            return RedirectToAction(
                nameof(Register));
        }

        ViewBag.Email =
            pending.Email;

        // Development OTP is NEVER displayed in production.
        var showDevelopmentCode =
            bool.TryParse(
                _configuration["Otp:ShowDevelopmentCode"],
                out var showCode) &&
            showCode;

        var environment =
            HttpContext.RequestServices
                .GetRequiredService<IWebHostEnvironment>();

        ViewBag.DevelopmentOtp =
            showDevelopmentCode &&
            environment.IsDevelopment()
                ? pending.DevelopmentCode
                : null;

        return View();
    }


    // =========================================================
    // VERIFY OTP - POST
    // =========================================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> VerifyOtp(
        string code)
    {
        var pending =
            GetPendingRegistration();

        if (pending == null)
        {
            TempData["Error"] =
                "Your verification session has expired. " +
                "Please register again.";

            return RedirectToAction(
                nameof(Register));
        }


        // -----------------------------------------------------
        // Check expiry.
        // -----------------------------------------------------

        if (pending.ExpiresUtc <= DateTime.UtcNow)
        {
            TempData["Error"] =
                "This verification code has expired. " +
                "Please request a new code.";

            return RedirectToAction(
                nameof(VerifyOtp));
        }


        // -----------------------------------------------------
        // Maximum 5 attempts.
        // -----------------------------------------------------

        if (pending.Attempts >= 5)
        {
            TempData["Error"] =
                "Too many incorrect attempts. " +
                "Please request a new code.";

            return RedirectToAction(
                nameof(VerifyOtp));
        }


        code =
            (code ?? string.Empty).Trim();


        // -----------------------------------------------------
        // Validate six-digit code.
        // -----------------------------------------------------

        var suppliedHash =
            HashOtp(
                code,
                pending.Email);

        var validCode =
            code.Length == 6 &&
            code.All(char.IsDigit) &&
            SecureEquals(
                suppliedHash,
                pending.CodeHash);

        if (!validCode)
        {
            pending.Attempts++;

            SavePendingRegistration(
                pending);

            var remaining =
                Math.Max(
                    0,
                    5 - pending.Attempts);

            TempData["Error"] =
                $"Incorrect verification code. " +
                $"{remaining} attempt(s) remaining.";

            return RedirectToAction(
                nameof(VerifyOtp));
        }


        // -----------------------------------------------------
        // Find registered user.
        // -----------------------------------------------------

        var user =
            await _userManager.FindByIdAsync(
                pending.UserId);

        if (user == null)
        {
            HttpContext.Session.Remove(
                PendingRegistrationSessionKey);

            TempData["Error"] =
                "The registration session is no longer valid. " +
                "Please register again.";

            return RedirectToAction(
                nameof(Register));
        }


        // -----------------------------------------------------
        // Mark email as verified.
        // -----------------------------------------------------

        if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true;

            var updateResult =
                await _userManager.UpdateAsync(
                    user);

            if (!updateResult.Succeeded)
            {
                foreach (var error in updateResult.Errors)
                {
                    Console.Error.WriteLine(
                        $"Email verification update error: " +
                        $"{error.Description}");
                }

                TempData["Error"] =
                    "Your email could not be verified. " +
                    "Please try again.";

                return RedirectToAction(
                    nameof(VerifyOtp));
            }
        }


        // -----------------------------------------------------
        // Assign Customer role ONLY after verification.
        //
        // Customers cannot choose Admin/ProductManager during
        // registration.
        // -----------------------------------------------------

        if (!await _userManager.IsInRoleAsync(
                user,
                "Customer"))
        {
            var roleResult =
                await _userManager.AddToRoleAsync(
                    user,
                    "Customer");

            if (!roleResult.Succeeded)
            {
                foreach (var error in roleResult.Errors)
                {
                    Console.Error.WriteLine(
                        $"Customer role assignment error: " +
                        $"{error.Description}");
                }

                TempData["Error"] =
                    "Your email was verified, but your account " +
                    "could not be fully activated. Please contact support.";

                return RedirectToAction(
                    nameof(VerifyOtp));
            }
        }


        // -----------------------------------------------------
        // Remove OTP session.
        // -----------------------------------------------------

        HttpContext.Session.Remove(
            PendingRegistrationSessionKey);


        // -----------------------------------------------------
        // Sign user in ONLY after successful verification.
        // -----------------------------------------------------

        await _signInManager.SignInAsync(
            user,
            isPersistent: false);


        TempData["Success"] =
            "Your account has been verified successfully.";

        return RedirectToAction(
            "Index",
            "Home");
    }


    // =========================================================
    // RESEND OTP
    // =========================================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResendOtp()
    {
        var pending =
            GetPendingRegistration();

        if (pending == null)
        {
            TempData["Error"] =
                "Your verification session has expired. " +
                "Please register again.";

            return RedirectToAction(
                nameof(Register));
        }

        var user =
            await _userManager.FindByIdAsync(
                pending.UserId);

        if (user == null)
        {
            HttpContext.Session.Remove(
                PendingRegistrationSessionKey);

            return RedirectToAction(
                nameof(Register));
        }

        if (user.EmailConfirmed)
        {
            HttpContext.Session.Remove(
                PendingRegistrationSessionKey);

            TempData["Info"] =
                "This email address has already been verified. " +
                "Please sign in.";

            return RedirectToAction(
                nameof(Login));
        }

        try
        {
            await SendVerificationOtpAsync(
                user);

            TempData["Success"] =
                "A new verification code has been sent.";

            return RedirectToAction(
                nameof(VerifyOtp));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                "==================================================");

            Console.Error.WriteLine(
                "RESEND OTP ERROR");

            Console.Error.WriteLine(
                ex.ToString());

            Console.Error.WriteLine(
                "==================================================");

            TempData["Error"] =
                "We could not send a new verification code. " +
                "Please try again.";

            return RedirectToAction(
                nameof(VerifyOtp));
        }
    }


    // =========================================================
    // LOGOUT
    // =========================================================

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();

        return RedirectToAction(
            "Index",
            "Home");
    }


    // =========================================================
    // MY ACCOUNT
    // =========================================================

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var user =
            await _userManager.GetUserAsync(User);

        if (user == null)
            return Challenge();

        return View(user);
    }


    // =========================================================
    // MY ORDERS
    // =========================================================

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Orders()
    {
        var user =
            await _userManager.GetUserAsync(User);

        if (user == null)
            return Challenge();

        var orders =
            await _context.Orders
                .Where(o => o.UserId == user.Id)
                .Include(o => o.OrderItems)
                    .ThenInclude(oi => oi.Product)
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();

        return View(orders);
    }


    // =========================================================
    // ORDER DETAILS
    // =========================================================

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> OrderDetails(
        string orderNumber)
    {
        if (string.IsNullOrWhiteSpace(orderNumber))
            return NotFound();

        var user =
            await _userManager.GetUserAsync(User);

        if (user == null)
            return Challenge();

        var order =
            await _context.Orders
                .Include(o => o.OrderItems)
                    .ThenInclude(oi => oi.Product)
                .FirstOrDefaultAsync(
                    o =>
                        o.OrderNumber == orderNumber &&
                        o.UserId == user.Id);

        if (order == null)
            return NotFound();

        return View(order);
    }


    // =========================================================
    // CANCEL ORDER
    // =========================================================

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelOrder(
        string orderNumber)
    {
        if (string.IsNullOrWhiteSpace(orderNumber))
            return NotFound();

        var user =
            await _userManager.GetUserAsync(User);

        if (user == null)
            return Challenge();

        var order =
            await _context.Orders
                .Include(o => o.OrderItems)
                    .ThenInclude(oi => oi.Product)
                .FirstOrDefaultAsync(
                    o =>
                        o.OrderNumber == orderNumber &&
                        o.UserId == user.Id);

        if (order == null)
            return NotFound();


        if (order.Status == OrderStatus.Cancelled)
        {
            TempData["Error"] =
                "This order has already been cancelled.";

            return RedirectToAction(
                nameof(OrderDetails),
                new
                {
                    orderNumber = order.OrderNumber
                });
        }


        if (order.Status != OrderStatus.Pending &&
            order.Status != OrderStatus.Confirmed)
        {
            TempData["Error"] =
                "This order can no longer be cancelled.";

            return RedirectToAction(
                nameof(OrderDetails),
                new
                {
                    orderNumber = order.OrderNumber
                });
        }


        if (string.Equals(
                order.PaymentStatus,
                "Paid",
                StringComparison.OrdinalIgnoreCase))
        {
            TempData["Error"] =
                "This paid order cannot be cancelled automatically. " +
                "Refund processing is required.";

            return RedirectToAction(
                nameof(OrderDetails),
                new
                {
                    orderNumber = order.OrderNumber
                });
        }


        // -----------------------------------------------------
        // Restore stock.
        // -----------------------------------------------------

        foreach (var item in
                 order.OrderItems ??
                 Enumerable.Empty<OrderItem>())
        {
            if (item.Product != null)
            {
                item.Product.StockQuantity +=
                    item.Quantity;

                item.Product.UpdatedAt =
                    DateTime.UtcNow;
            }
        }


        // -----------------------------------------------------
        // Cancel order.
        // -----------------------------------------------------

        order.Status =
            OrderStatus.Cancelled;

        order.CancelledBy =
            "Customer";

        order.UpdatedAt =
            DateTime.UtcNow;


        if (string.Equals(
                order.PaymentStatus,
                "Created",
                StringComparison.OrdinalIgnoreCase))
        {
            order.PaymentStatus =
                "Cancelled";
        }


        await _context.SaveChangesAsync();


        TempData["Success"] =
            "Your order has been cancelled successfully.";

        return RedirectToAction(
            nameof(OrderDetails),
            new
            {
                orderNumber = order.OrderNumber
            });
    }


    // =========================================================
    // ACCESS DENIED
    // =========================================================

    [HttpGet]
    public IActionResult AccessDenied(
        string? returnUrl = null)
    {
        ViewBag.ReturnUrl =
            returnUrl;

        return View();
    }


    // =========================================================
    // SEND VERIFICATION OTP
    // =========================================================

    private async Task SendVerificationOtpAsync(
        ApplicationUser user)
    {
        if (user == null)
            throw new ArgumentNullException(
                nameof(user));

        if (string.IsNullOrWhiteSpace(user.Email))
        {
            throw new InvalidOperationException(
                "The user does not have a valid email address.");
        }


        // -----------------------------------------------------
        // Read SMTP configuration.
        // -----------------------------------------------------

        var host =
            _configuration["Smtp:Host"];

        var from =
            _configuration["Smtp:From"];

        var password =
            _configuration["Smtp:Password"];


        if (string.IsNullOrWhiteSpace(host))
        {
            throw new InvalidOperationException(
                "SMTP host is not configured.");
        }

        if (string.IsNullOrWhiteSpace(from))
        {
            throw new InvalidOperationException(
                "SMTP sender email is not configured.");
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                "SMTP password is not configured.");
        }


        var port =
            int.TryParse(
                _configuration["Smtp:Port"],
                out var configuredPort)
                ? configuredPort
                : 587;


        var enableSsl =
            !string.Equals(
                _configuration["Smtp:EnableSsl"],
                "false",
                StringComparison.OrdinalIgnoreCase);


        var displayName =
            _configuration["Smtp:DisplayName"];

        if (string.IsNullOrWhiteSpace(displayName))
            displayName = "CrisKidsWear";


        // -----------------------------------------------------
        // Generate cryptographically secure 6-digit OTP.
        // -----------------------------------------------------

        var code =
            RandomNumberGenerator
                .GetInt32(
                    100000,
                    1000000)
                .ToString();


        var email =
            user.Email.Trim();


        var pending =
            new PendingRegistration
            {
                UserId = user.Id,

                Email = email,

                CodeHash =
                    HashOtp(
                        code,
                        email),

                ExpiresUtc =
                    DateTime.UtcNow.AddMinutes(5),

                Attempts = 0,

                // Stored only for local development display.
                // It is never shown in production.
                DevelopmentCode = code
            };


        // -----------------------------------------------------
        // Send the email FIRST.
        //
        // We intentionally do NOT save the pending session
        // until SendMailAsync succeeds.
        // -----------------------------------------------------

        using var client =
            new SmtpClient(
                host,
                port)
            {
                EnableSsl = enableSsl,

                Credentials =
                    new NetworkCredential(
                        from,
                        password),

                DeliveryMethod =
                    SmtpDeliveryMethod.Network,

                Timeout = 30000
            };


        using var message =
            new MailMessage
            {
                From =
                    new MailAddress(
                        from,
                        displayName),

                Subject =
                    "CrisKidsWear email verification code",

                Body =
                    $"Your CrisKidsWear verification code is {code}." +
                    Environment.NewLine +
                    Environment.NewLine +
                    "This code expires in 5 minutes." +
                    Environment.NewLine +
                    Environment.NewLine +
                    "If you did not create a CrisKidsWear account, " +
                    "you can safely ignore this email.",

                IsBodyHtml = false
            };


        message.To.Add(email);


        // This throws if Gmail/SMTP fails.
        await client.SendMailAsync(
            message);


        // -----------------------------------------------------
        // Only save OTP session after the email was successfully
        // handed to the SMTP client.
        // -----------------------------------------------------

        SavePendingRegistration(
            pending);


        Console.WriteLine(
            $"Verification OTP sent successfully to {email}.");
    }


    // =========================================================
    // GET PENDING REGISTRATION
    // =========================================================

    private PendingRegistration? GetPendingRegistration()
    {
        var json =
            HttpContext.Session.GetString(
                PendingRegistrationSessionKey);

        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer
                .Deserialize<PendingRegistration>(
                    json);
        }
        catch
        {
            HttpContext.Session.Remove(
                PendingRegistrationSessionKey);

            return null;
        }
    }


    // =========================================================
    // SAVE PENDING REGISTRATION
    // =========================================================

    private void SavePendingRegistration(
        PendingRegistration pending)
    {
        HttpContext.Session.SetString(
            PendingRegistrationSessionKey,
            JsonSerializer.Serialize(pending));
    }


    // =========================================================
    // HASH OTP
    // =========================================================

    private static string HashOtp(
        string code,
        string email)
    {
        var input =
            $"{email.Trim().ToLowerInvariant()}:{code}";

        var bytes =
            SHA256.HashData(
                Encoding.UTF8.GetBytes(input));

        return Convert.ToHexString(
            bytes);
    }


    // =========================================================
    // CONSTANT-TIME STRING COMPARISON
    // =========================================================

    private static bool SecureEquals(
        string a,
        string b)
    {
        var left =
            Encoding.UTF8.GetBytes(a);

        var right =
            Encoding.UTF8.GetBytes(b);

        return
            left.Length == right.Length &&
            CryptographicOperations
                .FixedTimeEquals(
                    left,
                    right);
    }


    // =========================================================
    // PENDING REGISTRATION MODEL
    // =========================================================

    private sealed class PendingRegistration
    {
        public string UserId { get; set; } =
            string.Empty;

        public string Email { get; set; } =
            string.Empty;

        public string CodeHash { get; set; } =
            string.Empty;

        public DateTime ExpiresUtc { get; set; }

        public int Attempts { get; set; }

        public string DevelopmentCode { get; set; } =
            string.Empty;
    }
}


// =============================================================
// LOGIN VIEW MODEL
// =============================================================

public class LoginViewModel
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } =
        string.Empty;

    [Required]
    [DataType(DataType.Password)]
    public string Password { get; set; } =
        string.Empty;

    [Display(Name = "Remember me")]
    public bool RememberMe { get; set; }

    public string? ReturnUrl { get; set; }
}


// =============================================================
// REGISTER VIEW MODEL
// =============================================================

public class RegisterViewModel
{
    [Required]
    [StringLength(100)]
    [Display(Name = "First name")]
    public string FirstName { get; set; } =
        string.Empty;


    [Required]
    [StringLength(100)]
    [Display(Name = "Last name")]
    public string LastName { get; set; } =
        string.Empty;


    [Required]
    [EmailAddress]
    public string Email { get; set; } =
        string.Empty;


    [Phone]
    [Display(Name = "Phone number")]
    public string? PhoneNumber { get; set; }


    [Required]
    [StringLength(
        100,
        MinimumLength = 6)]
    [DataType(DataType.Password)]
    public string Password { get; set; } =
        string.Empty;


    [Required]
    [DataType(DataType.Password)]
    [Compare("Password")]
    [Display(Name = "Confirm password")]
    public string ConfirmPassword { get; set; } =
        string.Empty;
}