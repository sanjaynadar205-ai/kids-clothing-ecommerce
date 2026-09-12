using KidsWearStore.Data;
using KidsWearStore.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KidsWearStore.Controllers;

[Authorize]
public class WishlistController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    public WishlistController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    // GET: /Wishlist
    public async Task<IActionResult> Index()
    {
        var user = await _userManager.GetUserAsync(User);

        if (user == null)
        {
            return Challenge();
        }

        var wishlist = await _context.Wishlists
            .Include(w => w.Product)
            .ThenInclude(p => p!.Category)
            .Where(w => w.UserId == user.Id)
            .OrderByDescending(w => w.CreatedAt)
            .ToListAsync();

        return View(wishlist);
    }

    // POST: /Wishlist/Add
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(int productId)
    {
        var user = await _userManager.GetUserAsync(User);

        if (user == null)
        {
            return Challenge();
        }

        var product = await _context.Products
            .FirstOrDefaultAsync(p => p.Id == productId && p.IsActive);

        if (product == null)
        {
            TempData["WishlistError"] = "Product is not available.";
            return RedirectToAction("Index", "Products");
        }

        var alreadyExists = await _context.Wishlists
            .AnyAsync(w =>
                w.UserId == user.Id &&
                w.ProductId == productId);

        if (!alreadyExists)
        {
            var wishlist = new Wishlist
            {
                UserId = user.Id,
                ProductId = productId,
                CreatedAt = DateTime.UtcNow
            };

            _context.Wishlists.Add(wishlist);
            await _context.SaveChangesAsync();

            TempData["WishlistMessage"] = "Product added to your wishlist ❤️";
        }
        else
        {
            TempData["WishlistMessage"] = "Product is already in your wishlist.";
        }

        return RedirectToAction("Details", "Products", new { id = productId });
    }

    // POST: /Wishlist/Remove
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(int productId)
    {
        var user = await _userManager.GetUserAsync(User);

        if (user == null)
        {
            return Challenge();
        }

        var wishlistItem = await _context.Wishlists
            .FirstOrDefaultAsync(w =>
                w.UserId == user.Id &&
                w.ProductId == productId);

        if (wishlistItem != null)
        {
            _context.Wishlists.Remove(wishlistItem);
            await _context.SaveChangesAsync();

            TempData["WishlistMessage"] = "Product removed from your wishlist.";
        }

        return RedirectToAction(nameof(Index));
    }

    // POST: /Wishlist/Toggle
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(int productId, string? returnUrl = null)
    {
        var user = await _userManager.GetUserAsync(User);

        if (user == null)
        {
            return Challenge();
        }

        var product = await _context.Products
            .FirstOrDefaultAsync(p => p.Id == productId && p.IsActive);

        if (product == null)
        {
            TempData["WishlistError"] = "Product is not available.";
            return RedirectToAction("Index", "Products");
        }

        var wishlistItem = await _context.Wishlists
            .FirstOrDefaultAsync(w =>
                w.UserId == user.Id &&
                w.ProductId == productId);

        if (wishlistItem == null)
        {
            _context.Wishlists.Add(new Wishlist
            {
                UserId = user.Id,
                ProductId = productId,
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            TempData["WishlistMessage"] = "Added to wishlist ❤️";
        }
        else
        {
            _context.Wishlists.Remove(wishlistItem);
            await _context.SaveChangesAsync();

            TempData["WishlistMessage"] = "Removed from wishlist.";
        }

        if (!string.IsNullOrWhiteSpace(returnUrl) &&
            Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        return RedirectToAction("Details", "Products", new { id = productId });
    }
}