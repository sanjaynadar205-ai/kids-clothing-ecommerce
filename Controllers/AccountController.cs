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
    private const string PendingRegistrationSessionKey = "KidsWear.PendingRegistration";
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

    [HttpGet]
    public IActionResult Login(string? returnUrl = null) =>
        View(new LoginViewModel { ReturnUrl = returnUrl });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var user = await _userManager.FindByEmailAsync(model.Email);
        if (user == null)
        {
            ModelState.AddModelError(string.Empty, "Invalid email or password.");
            return View(model);
        }

        var passwordValid = await _userManager.CheckPasswordAsync(user, model.Password);
        if (!passwordValid)
        {
            ModelState.AddModelError(string.Empty, "Invalid email or password.");
            return View(model);
        }

        await _signInManager.SignInAsync(user, model.RememberMe);

        if (!string.IsNullOrWhiteSpace(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
            return Redirect(model.ReturnUrl);

        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    public IActionResult Register() => View(new RegisterViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var existingUser = await _userManager.FindByEmailAsync(model.Email);
        if (existingUser != null)
        {
            if (!existingUser.EmailConfirmed)
            {
                await SendVerificationOtpAsync(existingUser);
                TempData["Info"] = "This account is awaiting email verification. A new code has been sent.";
                return RedirectToAction(nameof(VerifyOtp));
            }

            ModelState.AddModelError(nameof(model.Email), "An account with this email already exists.");
            return View(model);
        }

        var user = new ApplicationUser
        {
            UserName = model.Email.Trim(),
            Email = model.Email.Trim(),
            EmailConfirmed = false,
            FirstName = model.FirstName.Trim(),
            LastName = model.LastName.Trim(),
            PhoneNumber = string.IsNullOrWhiteSpace(model.PhoneNumber) ? null : model.PhoneNumber.Trim()
        };

        var result = await _userManager.CreateAsync(user, model.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return View(model);
        }

        await SendVerificationOtpAsync(user);
        return RedirectToAction(nameof(VerifyOtp));
    }

    [HttpGet]
    public IActionResult VerifyOtp()
    {
        var pending = GetPendingRegistration();
        if (pending == null) return RedirectToAction(nameof(Register));

        ViewBag.Email = pending.Email;
        ViewBag.DevelopmentOtp = _configuration["Otp:ShowDevelopmentCode"] == "true" &&
                                 HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment()
            ? pending.DevelopmentCode
            : null;

        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> VerifyOtp(string code)
    {
        var pending = GetPendingRegistration();
        if (pending == null) return RedirectToAction(nameof(Register));

        if (pending.ExpiresUtc <= DateTime.UtcNow)
        {
            TempData["Error"] = "This verification code has expired. Please request a new code.";
            return RedirectToAction(nameof(VerifyOtp));
        }

        if (pending.Attempts >= 5)
        {
            TempData["Error"] = "Too many incorrect attempts. Please request a new code.";
            return RedirectToAction(nameof(VerifyOtp));
        }

        code = (code ?? string.Empty).Trim();
        if (code.Length != 6 || !code.All(char.IsDigit) || !SecureEquals(HashOtp(code, pending.Email), pending.CodeHash))
        {
            pending.Attempts++;
            SavePendingRegistration(pending);
            TempData["Error"] = $"Incorrect verification code. {Math.Max(0, 5 - pending.Attempts)} attempt(s) remaining.";
            return RedirectToAction(nameof(VerifyOtp));
        }

        var user = await _userManager.FindByIdAsync(pending.UserId);
        if (user == null)
        {
            HttpContext.Session.Remove(PendingRegistrationSessionKey);
            TempData["Error"] = "The registration session is no longer valid. Please register again.";
            return RedirectToAction(nameof(Register));
        }

        user.EmailConfirmed = true;
        await _userManager.UpdateAsync(user);

        if (!await _userManager.IsInRoleAsync(user, "Customer"))
            await _userManager.AddToRoleAsync(user, "Customer");

        HttpContext.Session.Remove(PendingRegistrationSessionKey);
        await _signInManager.SignInAsync(user, isPersistent: false);

        TempData["Success"] = "Your account has been verified successfully.";
        return RedirectToAction("Index", "Home");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResendOtp()
    {
        var pending = GetPendingRegistration();
        if (pending == null) return RedirectToAction(nameof(Register));

        var user = await _userManager.FindByIdAsync(pending.UserId);
        if (user == null) return RedirectToAction(nameof(Register));

        await SendVerificationOtpAsync(user);
        TempData["Success"] = "A new verification code has been sent.";
        return RedirectToAction(nameof(VerifyOtp));
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        return View(user);
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Orders()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var orders = await _context.Orders
            .Where(o => o.UserId == user.Id)
            .Include(o => o.OrderItems).ThenInclude(oi => oi.Product)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();

        return View(orders);
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> OrderDetails(string orderNumber)
    {
        if (string.IsNullOrWhiteSpace(orderNumber)) return NotFound();
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var order = await _context.Orders
            .Include(o => o.OrderItems).ThenInclude(oi => oi.Product)
            .FirstOrDefaultAsync(o => o.OrderNumber == orderNumber && o.UserId == user.Id);

        return order == null ? NotFound() : View(order);
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelOrder(string orderNumber)
    {
        if (string.IsNullOrWhiteSpace(orderNumber)) return NotFound();
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var order = await _context.Orders
            .Include(o => o.OrderItems).ThenInclude(oi => oi.Product)
            .FirstOrDefaultAsync(o => o.OrderNumber == orderNumber && o.UserId == user.Id);

        if (order == null) return NotFound();
        if (order.Status == OrderStatus.Cancelled)
        {
            TempData["Error"] = "This order has already been cancelled.";
            return RedirectToAction(nameof(OrderDetails), new { orderNumber = order.OrderNumber });
        }
        if (order.Status != OrderStatus.Pending && order.Status != OrderStatus.Confirmed)
        {
            TempData["Error"] = "This order can no longer be cancelled.";
            return RedirectToAction(nameof(OrderDetails), new { orderNumber = order.OrderNumber });
        }
        if (string.Equals(order.PaymentStatus, "Paid", StringComparison.OrdinalIgnoreCase))
        {
            TempData["Error"] = "This paid order cannot be cancelled automatically. Refund processing is required.";
            return RedirectToAction(nameof(OrderDetails), new { orderNumber = order.OrderNumber });
        }

        foreach (var item in order.OrderItems ?? Enumerable.Empty<OrderItem>())
        {
            if (item.Product != null)
            {
                item.Product.StockQuantity += item.Quantity;
                item.Product.UpdatedAt = DateTime.UtcNow;
            }
        }

        order.Status = OrderStatus.Cancelled;
        order.CancelledBy = "Customer";
        order.UpdatedAt = DateTime.UtcNow;
        if (string.Equals(order.PaymentStatus, "Created", StringComparison.OrdinalIgnoreCase))
            order.PaymentStatus = "Cancelled";

        await _context.SaveChangesAsync();
        TempData["Success"] = "Your order has been cancelled successfully.";
        return RedirectToAction(nameof(OrderDetails), new { orderNumber = order.OrderNumber });
    }

    [HttpGet]
    public IActionResult AccessDenied(string? returnUrl = null)
    {
        ViewBag.ReturnUrl = returnUrl;
        return View();
    }

    private async Task SendVerificationOtpAsync(ApplicationUser user)
    {
        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        var pending = new PendingRegistration
        {
            UserId = user.Id,
            Email = user.Email ?? string.Empty,
            CodeHash = HashOtp(code, user.Email ?? string.Empty),
            ExpiresUtc = DateTime.UtcNow.AddMinutes(5),
            Attempts = 0,
            DevelopmentCode = code
        };

        SavePendingRegistration(pending);

        var host = _configuration["Smtp:Host"];
        var from = _configuration["Smtp:From"];
        var password = _configuration["Smtp:Password"];
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(password))
            return;

        var port = int.TryParse(_configuration["Smtp:Port"], out var p) ? p : 587;
        var enableSsl = !string.Equals(_configuration["Smtp:EnableSsl"], "false", StringComparison.OrdinalIgnoreCase);
        var displayName = _configuration["Smtp:DisplayName"] ?? "KidsWearStore";

        using var client = new SmtpClient(host, port)
        {
            EnableSsl = enableSsl,
            Credentials = new NetworkCredential(from, password)
        };
        using var message = new MailMessage
        {
            From = new MailAddress(from, displayName),
            Subject = "Your KidsWearStore verification code",
            Body = $"Your KidsWearStore verification code is {code}. It expires in 5 minutes. If you did not create this account, you can ignore this email."
        };
        message.To.Add(user.Email!);
        await client.SendMailAsync(message);
    }

    private PendingRegistration? GetPendingRegistration()
    {
        var json = HttpContext.Session.GetString(PendingRegistrationSessionKey);
        return string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<PendingRegistration>(json);
    }

    private void SavePendingRegistration(PendingRegistration pending) =>
        HttpContext.Session.SetString(PendingRegistrationSessionKey, JsonSerializer.Serialize(pending));

    private static string HashOtp(string code, string email)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{email.Trim().ToLowerInvariant()}:{code}"));
        return Convert.ToHexString(bytes);
    }

    private static bool SecureEquals(string a, string b)
    {
        var left = Encoding.UTF8.GetBytes(a);
        var right = Encoding.UTF8.GetBytes(b);
        return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
    }

    private sealed class PendingRegistration
    {
        public string UserId { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string CodeHash { get; set; } = string.Empty;
        public DateTime ExpiresUtc { get; set; }
        public int Attempts { get; set; }
        public string DevelopmentCode { get; set; } = string.Empty;
    }
}

public class LoginViewModel
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required, DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Remember me")]
    public bool RememberMe { get; set; }

    public string? ReturnUrl { get; set; }
}

public class RegisterViewModel
{
    [Required, StringLength(100), Display(Name = "First name")]
    public string FirstName { get; set; } = string.Empty;

    [Required, StringLength(100), Display(Name = "Last name")]
    public string LastName { get; set; } = string.Empty;

    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Phone, Display(Name = "Phone number")]
    public string? PhoneNumber { get; set; }

    [Required, StringLength(100, MinimumLength = 6), DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), Compare("Password"), Display(Name = "Confirm password")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
