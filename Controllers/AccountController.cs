using System.ComponentModel.DataAnnotations;
using KidsWearStore.Data;
using KidsWearStore.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KidsWearStore.Controllers;

public class AccountController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;

    public AccountController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager)
    {
        _context = context;
        _userManager = userManager;
        _signInManager = signInManager;
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

        var existingUser =
            await _userManager.FindByEmailAsync(email);

        if (existingUser != null)
        {
            ModelState.AddModelError(
                nameof(model.Email),
                "An account with this email already exists. Please sign in.");

            return View(model);
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
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

        var roleResult =
            await _userManager.AddToRoleAsync(
                user,
                "Customer");

        if (!roleResult.Succeeded)
        {
            foreach (var error in roleResult.Errors)
            {
                Console.Error.WriteLine(
                    $"Customer role assignment error: {error.Description}");
            }

            await _userManager.DeleteAsync(user);

            ModelState.AddModelError(
                string.Empty,
                "Your account could not be created completely. Please try again.");

            return View(model);
        }

        await _signInManager.SignInAsync(
            user,
            isPersistent: false);

        TempData["Success"] =
            "Your account has been created successfully.";

        return RedirectToAction(
            "Index",
            "Home");
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